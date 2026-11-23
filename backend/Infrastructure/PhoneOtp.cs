using System.Security.Cryptography;
using Ecommerce.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Api.Infrastructure
{
    public enum OtpResult { Ok, Invalid, TooManyAttempts }

    /// 6-digit codes, stored only as a salted hash, valid 10 minutes, 5 tries each.
    public static class PhoneOtp
    {
        public static readonly TimeSpan Life = TimeSpan.FromMinutes(10);
        public const int MaxAttempts = 5;

        private static string Hash(int companyId, string phone, string purpose, string code) =>
            Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{companyId}|{phone}|{purpose}|{code}")));

        public static async Task<string> IssueAsync(ApplicationDbContext db, int companyId, string phone, string purpose)
        {
            var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            // A new code cancels older unused ones.
            await db.PhoneOtps.Where(o => o.Phone == phone && o.Purpose == purpose && o.UsedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.UsedAt, DateTime.UtcNow));
            db.PhoneOtps.Add(new PhoneOtpRow
            {
                CompanyId = companyId, Phone = phone, Purpose = purpose,
                CodeHash = Hash(companyId, phone, purpose, code), ExpiresAt = DateTime.UtcNow.Add(Life)
            });
            await db.SaveChangesAsync();
            return code;
        }

        public static async Task<OtpResult> VerifyAsync(ApplicationDbContext db, int companyId, string phone, string purpose, string? code)
        {
            var row = await db.PhoneOtps
                .Where(o => o.Phone == phone && o.Purpose == purpose && o.UsedAt == null && o.ExpiresAt > DateTime.UtcNow)
                .OrderByDescending(o => o.CreatedDate).FirstOrDefaultAsync();
            if (row == null) return OtpResult.Invalid;
            if (row.Attempts >= MaxAttempts) return OtpResult.TooManyAttempts;

            // Count the attempt first so parallel guesses can't beat the limit.
            row.Attempts++;
            await db.SaveChangesAsync();

            var ok = code != null && code.Length == 6 &&
                CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(row.CodeHash),
                    System.Text.Encoding.UTF8.GetBytes(Hash(companyId, phone, purpose, code)));
            if (!ok) return row.Attempts >= MaxAttempts ? OtpResult.TooManyAttempts : OtpResult.Invalid;

            row.UsedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return OtpResult.Ok;
        }
    }
}
