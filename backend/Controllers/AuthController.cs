using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Ecommerce.Api.Domain;
using Ecommerce.Api.Infrastructure;
using Ecommerce.Api.Models;
using Microsoft.AspNetCore.Authorization;

namespace Ecommerce.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _config;
        private readonly IFileTextLogger _fileLogger;
        private readonly Microsoft.Extensions.Caching.Memory.IMemoryCache _memCache;

        public AuthController(ApplicationDbContext context, IConfiguration config, IFileTextLogger fileLogger, Microsoft.Extensions.Caching.Memory.IMemoryCache memCache)
        {
            _context = context;
            _config = config;
            _fileLogger = fileLogger;
            _memCache = memCache;
        }

        [HttpPost("register-company")]
        public async Task<IActionResult> RegisterCompany([FromBody] CompanyRegisterDto dto)
        {
            if (await _context.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == dto.OwnerEmail))
                return BadRequest(new { message = "Email already registered" });

            var subdomain = dto.Subdomain.ToLower().Replace(" ", "");
            if (await _context.Companies.AnyAsync(c => c.Subdomain == subdomain))
                return BadRequest(new { message = $"Subdomain '{subdomain}' is already taken." });

            var basicPlan = await _context.SubscriptionPlans.FirstOrDefaultAsync(p => p.Name == "Basic Plan");
            var role = await _context.Roles.FirstOrDefaultAsync(r => r.Name == "companyadmin");
            var passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);

            var companyId = (await _context.Database.SqlQueryRaw<int>(
                "SELECT sp_register_company({0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12})",
                dto.CompanyName, subdomain, dto.Address, dto.Division, dto.District, dto.Thana,
                dto.OwnerFirstName, dto.OwnerLastName, dto.OwnerEmail, dto.OwnerPhone, passwordHash,
                basicPlan?.Id ?? 0, role?.Id ?? 0
            ).ToListAsync()).FirstOrDefault();

            return Ok(new { message = "Company registration successful. Awaiting Super Admin approval.", email = dto.OwnerEmail, companyId });
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterDto dto)
        {
            if (await _context.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == dto.Email))
                return BadRequest(new { message = "Email already registered" });

            if (!string.IsNullOrEmpty(dto.CompanyName))
            {
                var subdomain = dto.Subdomain ?? dto.CompanyName.ToLower().Replace(" ", "");
                if (await _context.Companies.AnyAsync(c => c.Subdomain == subdomain))
                    return BadRequest(new { message = $"Subdomain '{subdomain}' is already taken." });

                var basicPlan = await _context.SubscriptionPlans.FirstOrDefaultAsync(p => p.Name == "Basic Plan");
                var role = await _context.Roles.FirstOrDefaultAsync(r => r.Name == "companyadmin");
                var passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);

                var companyId = (await _context.Database.SqlQueryRaw<int>(
                    "SELECT sp_register_company({0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12})",
                    dto.CompanyName, subdomain, "", "", "", "",
                    dto.FirstName, dto.LastName, dto.Email, dto.PhoneNumber ?? "", passwordHash,
                    basicPlan?.Id ?? 0, role?.Id ?? 0
                ).ToListAsync()).FirstOrDefault();

                return Ok(new { message = "Registration successful. Awaiting Super Admin approval.", email = dto.Email, companyId });
            }
            else
            {
                var role = await _context.Roles.FirstOrDefaultAsync(r => r.Name == "salesstaff");
                var passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);

                await _context.Database.SqlQueryRaw<int>(
                    "SELECT sp_create_user({0},{1},{2},{3},{4},{5},{6},{7},{8},{9})",
                    null, dto.Email, passwordHash, dto.FirstName, dto.LastName,
                    dto.PhoneNumber ?? "", "salesstaff", true, role?.Id, null
                ).ToListAsync();

                return Ok(new { message = "Registration successful", email = dto.Email, companyId = (int?)null });
            }
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            try
            {
                var user = await _context.Users
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                    .FirstOrDefaultAsync(u => u.Email == dto.Email);

                if (user == null)
                {
                    _fileLogger.LogError("LOGIN", $"User not found: '{dto.Email}'");
                    return Unauthorized(new { message = "Invalid email or password" });
                }

                if (dto.LoginContext == "admin")
                {
                    var userRoles = user.UserRoles.Select(ur => ur.Role!.Name).ToList();
                    if (!string.Equals(user.UserType, "superadmin", StringComparison.OrdinalIgnoreCase) && !userRoles.Contains("superadmin"))
                        return Unauthorized(new { message = "Only platform administrators can log in here." });
                }
                else if (!string.IsNullOrEmpty(dto.LoginContext))
                {
                    var company = await _context.Companies.IgnoreQueryFilters()
                        .FirstOrDefaultAsync(c => EF.Functions.ILike(c.Subdomain, dto.LoginContext));
                    if (company == null || user.CompanyId != company.Id)
                        return Unauthorized(new { message = "You do not have access to this store dashboard." });
                }

                if (!BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
                    return Unauthorized(new { message = "Invalid email or password" });

                if (!user.IsActive)
                    return BadRequest(new { message = "User account is suspended" });

                var roles = user.UserRoles.Select(ur => ur.Role!.Name).ToList();
                var token = GenerateJwtToken(user, roles);

                // Invalidate previous sessions
                var oldSessions = await _context.UserSessions.Where(s => s.UserId == user.Id && s.IsActive).ToListAsync();
                foreach (var old in oldSessions) old.IsActive = false;

                var sessionToken = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
                _context.UserSessions.Add(new UserSession
                {
                    SessionToken = sessionToken,
                    UserId       = user.Id,
                    CompanyId    = user.CompanyId,
                    Roles        = string.Join(",", roles),
                    UserType     = user.UserType,
                    Email        = user.Email,
                    FullName     = $"{user.FirstName} {user.LastName}",
                    CreatedAt    = DateTime.UtcNow,
                    ExpiresAt    = DateTime.UtcNow.AddHours(24),
                    IsActive     = true,
                    IpAddress    = HttpContext.Connection.RemoteIpAddress?.ToString(),
                    UserAgent    = HttpContext.Request.Headers["User-Agent"].ToString()
                });
                await _context.SaveChangesAsync();

                _fileLogger.LogInfo("LOGIN", $"Login success: '{dto.Email}'");
                return Ok(new LoginResponse(token, sessionToken, user.Email, $"{user.FirstName} {user.LastName}", user.CompanyId, roles, user.UserType));
            }
            catch (Exception ex)
            {
                _fileLogger.LogError("LOGIN", $"Unhandled exception for '{dto.Email}'", ex);
                throw;
            }
        }

        [HttpPost("logout")]
        [Authorize]
        public async Task<IActionResult> Logout()
        {
            var authHeader = HttpContext.Request.Headers["Authorization"].ToString();
            if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = authHeader[7..].Trim();
                var session = await _context.UserSessions.FirstOrDefaultAsync(s => s.SessionToken == token);
                if (session != null) { session.IsActive = false; await _context.SaveChangesAsync(); }
                _memCache.Remove($"session:{token}");
            }
            return Ok(new { message = "Logged out successfully." });
        }

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto)
        {
            var user = await _context.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Email == dto.Email);
            if (user == null) return BadRequest(new { message = "User with this email does not exist." });

            var otpCode = new Random().Next(100000, 999999).ToString();
            var expiresAt = DateTime.UtcNow.AddMinutes(15);

            await _context.Database.ExecuteSqlRawAsync(
                "CALL sp_generate_otp({0},{1},{2})", dto.Email, otpCode, expiresAt);

            Console.WriteLine($"[OTP SIMULATOR] Generated OTP {otpCode} for email {dto.Email}");
            await SendOtpEmailAsync(dto.Email, otpCode);

            return Ok(new { message = "Verification OTP code successfully generated and sent to your email." });
        }

        [HttpPost("reset-password-otp")]
        public async Task<IActionResult> ResetPasswordOtp([FromBody] ResetPasswordOtpDto dto)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);
            if (user == null) return BadRequest(new { message = "User with this email does not exist." });

            if (string.IsNullOrEmpty(user.Otp) || user.Otp != dto.Otp ||
                !user.OtpExpiresAt.HasValue || user.OtpExpiresAt.Value < DateTime.UtcNow)
                return BadRequest(new { message = "Invalid or expired OTP verification code." });

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
            user.Otp = null;
            user.OtpExpiresAt = null;
            await _context.SaveChangesAsync();

            return Ok(new { message = "Password reset completed successfully." });
        }

        private string GenerateJwtToken(User user, List<string> roles)
        {
            var keyStr = _config["Jwt:Secret"] ?? "super-secret-key-change-in-prod-long-enough-32-chars";
            var key    = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyStr));
            var creds  = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new(JwtRegisteredClaimNames.Email, user.Email),
                new("name", $"{user.FirstName} {user.LastName}")
            };
            if (user.CompanyId.HasValue)
                claims.Add(new Claim("company_id", user.CompanyId.Value.ToString()));
            foreach (var role in roles)
                claims.Add(new Claim(ClaimTypes.Role, role));

            var token = new JwtSecurityToken(claims: claims, expires: DateTime.UtcNow.AddHours(4), signingCredentials: creds);
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private async Task SendOtpEmailAsync(string toEmail, string otpCode)
        {
            try
            {
                var smtpHost    = _config["SmtpSettings:Host"] ?? "smtp.gmail.com";
                var smtpPort    = int.Parse(_config["SmtpSettings:Port"] ?? "587");
                var enableSsl   = bool.Parse(_config["SmtpSettings:EnableSsl"] ?? "true");
                var username    = _config["SmtpSettings:Username"] ?? "";
                var password    = _config["SmtpSettings:Password"] ?? "";
                var fromAddress = _config["SmtpSettings:FromAddress"] ?? "noreply@ghorerbazar.com";
                var fromName    = _config["SmtpSettings:FromName"] ?? "Ghorer Bazar Organic";

                using var client = new System.Net.Mail.SmtpClient(smtpHost, smtpPort)
                {
                    EnableSsl = enableSsl,
                    UseDefaultCredentials = false
                };
                if (!string.IsNullOrEmpty(username))
                    client.Credentials = new System.Net.NetworkCredential(username, password);

                var mail = new System.Net.Mail.MailMessage
                {
                    From = new System.Net.Mail.MailAddress(fromAddress, fromName),
                    Subject = "Verification Code for Password Recovery",
                    IsBodyHtml = true,
                    Body = $@"<div style='font-family:Arial,sans-serif;padding:20px;max-width:600px;margin:auto;border:1px solid #e2e8f0;border-radius:12px;'>
                        <div style='background:#0D5237;padding:15px;text-align:center;border-radius:8px 8px 0 0;'>
                            <h2 style='color:#fff;margin:0;'>Ghorer Bazar Organic</h2></div>
                        <div style='padding:20px;'>
                            <p>Your OTP code:</p>
                            <div style='text-align:center;margin:30px 0;'>
                                <span style='background:#f8fafc;border:2px dashed #0D5237;color:#0D5237;font-size:28px;font-weight:bold;padding:10px 24px;font-family:monospace;letter-spacing:4px;border-radius:6px;display:inline-block;'>{otpCode}</span>
                            </div>
                            <p>This code expires in 15 minutes.</p></div>
                        <div style='border-top:1px solid #e2e8f0;padding-top:15px;font-size:11px;color:#64748b;text-align:center;'>
                            &copy; {DateTime.UtcNow.Year} Ghorer Bazar Organic. All rights reserved.</div></div>"
                };
                mail.To.Add(toEmail);
                await client.SendMailAsync(mail);
            }
            catch (Exception ex)
            {
                _fileLogger.LogError("SMTP", $"Failed to send OTP to {toEmail}", ex);
            }
        }
    }
}
