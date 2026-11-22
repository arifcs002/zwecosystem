using System.Security.Cryptography;
using System.Text;
using Ecommerce.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Api.Infrastructure
{
    public interface ICustomerAuth
    {
        /// <summary>The logged-in storefront customer for this request, or null (guest / expired / wrong store).</summary>
        Task<CustomerAccount?> ResolveAsync(HttpRequest request);
    }

    /// <summary>
    /// Shopper authentication, deliberately separate from staff auth: the token travels in
    /// X-Customer-Token (never "Authorization: Bearer"), lives in customer_sessions, and is only
    /// consulted by shopper-facing code. Sessions are scoped to the store in X-Tenant-ID through the
    /// DbContext query filter, so a token from shop A is worthless against shop B.
    /// </summary>
    public class CustomerAuth : ICustomerAuth
    {
        public const string Header = "X-Customer-Token";
        private readonly ApplicationDbContext _context;
        public CustomerAuth(ApplicationDbContext context) { _context = context; }

        public async Task<CustomerAccount?> ResolveAsync(HttpRequest request)
        {
            if (!_context.CompanyId.HasValue) return null;             // no store => no shopper
            var token = request.Headers[Header].ToString().Trim();
            if (token.Length < 20 || token.Length > 128) return null;

            var hash = Hash(token);
            var session = await _context.CustomerSessions.AsNoTracking()
                .FirstOrDefaultAsync(s => s.TokenHash == hash && s.ExpiresAt > DateTime.UtcNow);
            if (session == null) return null;
            return await _context.CustomerAccounts.FirstOrDefaultAsync(a => a.Id == session.AccountId && a.IsActive);
        }

        public static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    public static class Audit
    {
        /// <summary>Records who did what. Never throws — a failed audit write must not fail the action itself.</summary>
        public static async Task LogAsync(ApplicationDbContext db, HttpContext http, string action, string? details = null)
        {
            try
            {
                var ua = http.Request.Headers.UserAgent.ToString();
                db.AuditLogs.Add(new AuditLog
                {
                    CompanyId = db.CompanyId,
                    UserId = db.CurrentUserId,
                    Action = action,
                    Details = details == null ? null : (details.Length > 400 ? details[..400] : details),
                    IpAddress = RateLimit.ClientKey(http),
                    UserAgent = ua.Length > 200 ? ua[..200] : ua
                });
                await db.SaveChangesAsync();
            }
            catch { /* best effort */ }
        }
    }
}
