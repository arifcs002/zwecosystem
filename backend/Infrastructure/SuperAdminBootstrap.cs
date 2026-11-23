using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Api.Infrastructure
{
    /// The seed data ships superadmin accounts whose password ("123456") is
    /// public in the source. If SUPERADMIN_PASSWORD is set, any superadmin still
    /// carrying that seed hash gets the new password. Accounts whose password
    /// was already changed are left alone, so this is safe to run every start.
    public static class SuperAdminBootstrap
    {
        public const string SeedHash = "$2b$11$f1FDqBdveY.KIotMM1fKM.OoZUEGh1tnXTAlWX6aGj3zZHsW2KCrK";
        public const int MinLength = 12;

        public static void Run(ApplicationDbContext db, string? newPassword, bool isProduction, Action<string> log)
        {
            if (string.IsNullOrEmpty(newPassword))
            {
                var stillSeed = db.Database.SqlQueryRaw<int>(
                    "SELECT COUNT(*)::int AS \"Value\" FROM users WHERE user_type='superadmin' AND password_hash={0}", SeedHash)
                    .AsEnumerable().FirstOrDefault();
                if (stillSeed > 0)
                    log($"--> WARNING: {stillSeed} superadmin account(s) still use the public seed password." +
                        (isProduction ? " Set SUPERADMIN_PASSWORD (12+ chars) and redeploy." : ""));
                return;
            }
            if (newPassword.Length < MinLength)
            {
                log($"--> SUPERADMIN_PASSWORD ignored: must be at least {MinLength} characters.");
                return;
            }
            var hash = BCrypt.Net.BCrypt.HashPassword(newPassword, 11);
            var n = db.Database.ExecuteSqlRaw(
                "UPDATE users SET password_hash={0} WHERE user_type='superadmin' AND password_hash={1}", hash, SeedHash);
            if (n > 0)
            {
                // Kill any session that was opened with the old public password.
                db.Database.ExecuteSqlRaw(
                    "DELETE FROM user_sessions WHERE user_id IN (SELECT id FROM users WHERE user_type='superadmin')");
                log($"--> Superadmin password rotated for {n} account(s).");
            }
        }
    }
}
