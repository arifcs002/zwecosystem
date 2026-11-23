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
        private readonly ISmsSender _sms;

        public AccountController(ApplicationDbContext context, ICustomerAuth auth, IMemoryCache cache, ISmsSender sms)
        {
            _context = context;
            _auth = auth;
            _cache = cache;
            _sms = sms;
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

        // ── SMS one-time codes ───────────────────────────────────────────────────
        // Sent through the shop's own gateway (company setting sms_api_url), same as order SMS.
        // The answer never reveals whether a number has an account.

        [HttpPost("otp/send")]
        public async Task<IActionResult> SendOtp([FromBody] OtpSendDto dto)
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest(new { message = "Store context is required." });
            var purpose = (dto.Purpose ?? "").ToLowerInvariant();
            if (purpose != "reset" && purpose != "verify") return BadRequest(new { message = "Unknown purpose." });

            string? phone;
            if (purpose == "verify")
            {
                var me = await _auth.ResolveAsync(Request);
                if (me == null) return Unauthorized(new { message = "Please log in." });
                phone = me.Phone;
            }
            else phone = PhoneUtil.NormalizeBd(dto.Phone);
            if (phone == null) return BadRequest(new { message = "Enter a valid Bangladeshi mobile number (01XXXXXXXXX)." });

            if (RateLimit.Exceeded(_cache, $"otp-client:{RateLimit.ClientKey(HttpContext)}", 10, TimeSpan.FromHours(1)) ||
                RateLimit.Exceeded(_cache, $"otp-phone:{companyId}:{phone}", 3, TimeSpan.FromMinutes(10)))
                return StatusCode(429, new { message = "Too many codes requested. Please wait a few minutes." });

            var apiUrl = await _context.CompanySettings.AsNoTracking()
                .Where(s => s.CompanyId == companyId.Value && s.Key == "sms_api_url").Select(s => s.Value).FirstOrDefaultAsync();
            if (string.IsNullOrWhiteSpace(apiUrl))
                return StatusCode(503, new { message = "SMS codes aren't available for this shop right now." });

            var generic = Ok(new { message = "If this number can receive a code, it is on its way.", expiresInMinutes = (int)PhoneOtp.Life.TotalMinutes });
            if (purpose == "reset")
            {
                var exists = await _context.CustomerAccounts.AnyAsync(a => a.Phone == phone && a.IsActive);
                if (!exists) return generic;
            }

            var code = await PhoneOtp.IssueAsync(_context, companyId.Value, phone, purpose);
            var shop = await _context.CompanySettings.AsNoTracking()
                .Where(s => s.CompanyId == companyId.Value && (s.Key == "shop_name" || s.Key == "store_name"))
                .Select(s => s.Value).FirstOrDefaultAsync();
            if (string.IsNullOrWhiteSpace(shop))
                shop = await _context.Companies.AsNoTracking().Where(c => c.Id == companyId.Value).Select(c => c.Name).FirstOrDefaultAsync();
            var from = string.IsNullOrWhiteSpace(shop) ? "" : $" ({shop})";
            await _sms.SendAsync(apiUrl, phone, $"{code} is your verification code{from}. It expires in {(int)PhoneOtp.Life.TotalMinutes} minutes.");
            return generic;
        }

        [HttpPost("password/reset")]
        public async Task<IActionResult> ResetPassword([FromBody] PasswordResetDto dto)
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest(new { message = "Store context is required." });
            var phone = PhoneUtil.NormalizeBd(dto.Phone);
            if (RateLimit.Exceeded(_cache, $"otp-reset:{RateLimit.ClientKey(HttpContext)}", 20, TimeSpan.FromHours(1)))
                return StatusCode(429, new { message = "Too many attempts. Please wait a while." });
            var pwError = PasswordError(dto.NewPassword);
            if (pwError != null) return BadRequest(new { message = pwError });

            const string bad = "That code is wrong or has expired.";
            if (phone == null) return BadRequest(new { message = bad });
            var res = await PhoneOtp.VerifyAsync(_context, companyId.Value, phone, "reset", dto.Code);
            if (res == OtpResult.TooManyAttempts) return StatusCode(429, new { message = "Too many wrong codes. Request a new one." });
            if (res != OtpResult.Ok) return BadRequest(new { message = bad });

            var account = await _context.CustomerAccounts.FirstOrDefaultAsync(a => a.Phone == phone && a.IsActive);
            if (account == null) return BadRequest(new { message = bad });
            account.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
            account.PhoneVerifiedAt ??= DateTime.UtcNow;     // the code proved they hold the phone
            account.UpdatedDate = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            await _context.CustomerSessions.Where(s => s.AccountId == account.Id).ExecuteDeleteAsync();   // sign out everywhere
            return Ok(new { message = "Password changed. Please log in." });
        }

        [HttpPost("phone/verify")]
        public async Task<IActionResult> VerifyPhone([FromBody] PhoneVerifyDto dto)
        {
            var account = await _auth.ResolveAsync(Request);
            if (account == null) return Unauthorized(new { message = "Please log in." });
            var res = await PhoneOtp.VerifyAsync(_context, account.CompanyId, account.Phone, "verify", dto.Code);
            if (res == OtpResult.TooManyAttempts) return StatusCode(429, new { message = "Too many wrong codes. Request a new one." });
            if (res != OtpResult.Ok) return BadRequest(new { message = "That code is wrong or has expired." });
            account.PhoneVerifiedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return Ok(await Profile(account));
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
            return new { a.Id, a.Name, a.Phone, a.Email, a.Address, a.District, a.Thana, group, phoneVerified = a.PhoneVerifiedAt != null };
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
