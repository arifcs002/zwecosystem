using System.Text.RegularExpressions;
using Ecommerce.Api.Domain;
using Ecommerce.Api.Infrastructure;
using Ecommerce.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Ecommerce.Api.Controllers
{
    // Storefront customer accounts. Everything here is anonymous-with-a-shopper-token: the store comes
    // from X-Tenant-ID and the shopper from X-Customer-Token (see CustomerAuth). None of it can reach
    // staff endpoints, which only honour "Authorization: Bearer <staff session>".
    [ApiController]
    [Route("api/account")]
    [AllowAnonymous]
    public class AccountController : ControllerBase
    {
        private const int MaxSessionsPerAccount = 5;
        private static readonly TimeSpan SessionLife = TimeSpan.FromDays(30);
        // A valid bcrypt hash of nothing in particular — verified against when the phone is unknown so
        // "no such account" and "wrong password" take the same time.
        private static readonly string DummyHash = BCrypt.Net.BCrypt.HashPassword("not-a-real-password-" + Guid.NewGuid());

        private readonly ApplicationDbContext _context;
        private readonly ICustomerAuth _auth;
        private readonly IMemoryCache _cache;

        public AccountController(ApplicationDbContext context, ICustomerAuth auth, IMemoryCache cache)
        {
            _context = context;
            _auth = auth;
            _cache = cache;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] AccountRegisterDto dto)
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest(new { message = "Store context is required." });
            if (RateLimit.Exceeded(_cache, $"acct-register:{RateLimit.ClientKey(HttpContext)}", 30, TimeSpan.FromHours(1)))
                return StatusCode(429, new { message = "Too many sign-ups from this device. Please try again later." });

            var name = (dto.Name ?? "").Trim();
            var phone = PhoneUtil.NormalizeBd(dto.Phone);
            var email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim();
            if (name.Length < 2 || name.Length > 80) return BadRequest(new { message = "Please enter your name." });
            if (phone == null) return BadRequest(new { message = "Enter a valid Bangladeshi mobile number (01XXXXXXXXX)." });
            var pwError = PasswordError(dto.Password);
            if (pwError != null) return BadRequest(new { message = pwError });
            if (email != null && (email.Length > 120 || !Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$")))
                return BadRequest(new { message = "That email address doesn't look right." });

            if (await _context.CustomerAccounts.AnyAsync(a => a.Phone == phone))
                return Conflict(new { message = "An account with this mobile number already exists. Please log in." });

            var account = new CustomerAccount
            {
                CompanyId = companyId.Value, Name = name, Phone = phone, Email = email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password), LastLogin = DateTime.UtcNow
            };
            _context.CustomerAccounts.Add(account);
            try { await _context.SaveChangesAsync(); }
            catch (DbUpdateException) { return Conflict(new { message = "An account with this mobile number already exists. Please log in." }); }

            return Ok(await StartSession(account));
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] AccountLoginDto dto)
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest(new { message = "Store context is required." });
            var phone = PhoneUtil.NormalizeBd(dto.Phone);
            // Throttle per device+number so a number can't be brute-forced.
            if (RateLimit.Exceeded(_cache, $"acct-login:{RateLimit.ClientKey(HttpContext)}:{phone}", 10, TimeSpan.FromMinutes(10)))
                return StatusCode(429, new { message = "Too many attempts. Please wait a few minutes." });

            const string bad = "Wrong mobile number or password.";
            var account = phone == null ? null : await _context.CustomerAccounts.FirstOrDefaultAsync(a => a.Phone == phone);
            var ok = BCrypt.Net.BCrypt.Verify(dto.Password ?? "", account?.PasswordHash ?? DummyHash);
            if (account == null || !ok) return Unauthorized(new { message = bad });
            if (!account.IsActive) return Unauthorized(new { message = "This account has been disabled. Please contact the shop." });

            account.LastLogin = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return Ok(await StartSession(account));
        }

        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            var token = Request.Headers[CustomerAuth.Header].ToString().Trim();
            if (token.Length > 0 && _context.CompanyId.HasValue)
            {
                var hash = CustomerAuth.Hash(token);
                await _context.CustomerSessions.Where(s => s.TokenHash == hash).ExecuteDeleteAsync();
            }
            return Ok(new { message = "Logged out." });
        }

        [HttpGet("me")]
        public async Task<IActionResult> Me()
        {
            var account = await _auth.ResolveAsync(Request);
            return account == null ? Unauthorized(new { message = "Please log in." }) : Ok(await Profile(account));
        }

        [HttpPut("me")]
        public async Task<IActionResult> Update([FromBody] AccountUpdateDto dto)
        {
            var account = await _auth.ResolveAsync(Request);
            if (account == null) return Unauthorized(new { message = "Please log in." });

            var name = (dto.Name ?? "").Trim();
            if (name.Length < 2 || name.Length > 80) return BadRequest(new { message = "Please enter your name." });
            var email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim();
            if (email != null && (email.Length > 120 || !Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$")))
                return BadRequest(new { message = "That email address doesn't look right." });

            account.Name = name;
            account.Email = email;
            account.Address = Clip(dto.Address, 400);
            account.District = Clip(dto.District, 80);
            account.Thana = Clip(dto.Thana, 80);
            account.UpdatedDate = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return Ok(await Profile(account));
        }

        [HttpPut("password")]
        public async Task<IActionResult> ChangePassword([FromBody] AccountPasswordDto dto)
        {
            var account = await _auth.ResolveAsync(Request);
            if (account == null) return Unauthorized(new { message = "Please log in." });
            if (RateLimit.Exceeded(_cache, $"acct-pw:{account.Id}", 10, TimeSpan.FromMinutes(10)))
                return StatusCode(429, new { message = "Too many attempts. Please wait a few minutes." });
            if (!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword ?? "", account.PasswordHash))
                return BadRequest(new { message = "Your current password is incorrect." });
            var pwError = PasswordError(dto.NewPassword);
            if (pwError != null) return BadRequest(new { message = pwError });

            account.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
            account.UpdatedDate = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // Sign every other device out; keep this one.
            var keep = CustomerAuth.Hash(Request.Headers[CustomerAuth.Header].ToString().Trim());
            await _context.CustomerSessions.Where(s => s.AccountId == account.Id && s.TokenHash != keep).ExecuteDeleteAsync();
            return Ok(new { message = "Password changed." });
        }

        [HttpGet("orders")]
        public async Task<IActionResult> Orders()
        {
            var account = await _auth.ResolveAsync(Request);
            if (account == null) return Unauthorized(new { message = "Please log in." });
            var rows = await _context.Orders.AsNoTracking()
                .Where(o => o.CustomerAccountId == account.Id)
                .OrderByDescending(o => o.CreatedDate).Take(50)
                .Select(o => new { o.OrderNumber, o.Status, o.Total, o.Discount, o.DiscountLabel, date = o.CreatedDate, o.TrackingNumber, o.CourierName })
                .ToListAsync();
            return Ok(rows);
        }

        // ── helpers ──────────────────────────────────────────────────────────────

        private async Task<object> StartSession(CustomerAccount account)
        {
            var token = CustomerAuth.NewToken();
            _context.CustomerSessions.Add(new CustomerSession
            {
                CompanyId = account.CompanyId, AccountId = account.Id, TokenHash = CustomerAuth.Hash(token),
                ExpiresAt = DateTime.UtcNow.Add(SessionLife), IpAddress = RateLimit.ClientKey(HttpContext),
                UserAgent = Clip(Request.Headers.UserAgent.ToString(), 200)
            });
            await _context.SaveChangesAsync();

            // Keep only the newest few sessions per account.
            var old = await _context.CustomerSessions.Where(s => s.AccountId == account.Id)
                .OrderByDescending(s => s.CreatedDate).Skip(MaxSessionsPerAccount).Select(s => s.Id).ToListAsync();
            if (old.Count > 0) await _context.CustomerSessions.Where(s => old.Contains(s.Id)).ExecuteDeleteAsync();

            return new { token, account = await Profile(account) };
        }

        private async Task<object> Profile(CustomerAccount a)
        {
            var group = a.GroupId == null ? null
                : await _context.CustomerGroups.AsNoTracking().Where(g => g.Id == a.GroupId)
                    .Select(g => new { g.Name, g.DiscountPercent }).FirstOrDefaultAsync();
            return new { a.Id, a.Name, a.Phone, a.Email, a.Address, a.District, a.Thana, group };
        }

        private static string? PasswordError(string? pw)
        {
            if (string.IsNullOrEmpty(pw) || pw.Length < 8) return "Password must be at least 8 characters.";
            if (pw.Length > 72) return "Password is too long (max 72 characters).";   // bcrypt's input limit
            return null;
        }

        private static string? Clip(string? s, int max)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim();
            return s.Length > max ? s[..max] : s;
        }
    }
}
