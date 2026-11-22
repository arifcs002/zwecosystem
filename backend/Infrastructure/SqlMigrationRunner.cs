using System.Data.Common;

namespace Ecommerce.Api.Infrastructure
{
    /// <summary>
    /// Versioned SQL migrations. Every *.sql file in Migrations/Sql is applied exactly
    /// once, in file-name order, inside its own transaction, and recorded in
    /// schema_migrations. Scripts must be idempotent-friendly (IF NOT EXISTS) because on a
    /// brand-new database EnsureCreated() has already built the tables from the model.
    ///
    /// Why: the legacy startup DDL in Program.cs is fire-and-forget and can't express
    /// "this change was applied". New schema changes go here instead, as a new numbered file
    /// (0002_..., 0003_...). NEVER edit a script that has already shipped.
    /// </summary>
    public static class SqlMigrationRunner
    {
        private const long AdvisoryLockKey = 727274001; // serialises concurrent app starts

        public static int Run(DbConnection conn, string? directory, Action<string> log)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                log($"--> SQL migrations folder not found, skipping ({directory ?? "null"}).");
                return 0;
            }

            if (conn.State != System.Data.ConnectionState.Open) conn.Open();

            Exec(conn, null, $"SELECT pg_advisory_lock({AdvisoryLockKey})");
            try
            {
                Exec(conn, null, @"CREATE TABLE IF NOT EXISTS schema_migrations (
                                       version    TEXT PRIMARY KEY,
                                       applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW())");

                var applied = new HashSet<string>(StringComparer.Ordinal);
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT version FROM schema_migrations";
                    using var rd = cmd.ExecuteReader();
                    while (rd.Read()) applied.Add(rd.GetString(0));
                }

                var count = 0;
                foreach (var file in Directory.GetFiles(directory, "*.sql").OrderBy(f => f, StringComparer.Ordinal))
                {
                    var version = Path.GetFileName(file);
                    if (applied.Contains(version)) continue;

                    log($"--> Applying SQL migration {version} ...");
                    using var tx = conn.BeginTransaction();
                    try
                    {
                        Exec(conn, tx, File.ReadAllText(file));
                        using (var ins = conn.CreateCommand())
                        {
                            ins.Transaction = tx;
                            ins.CommandText = "INSERT INTO schema_migrations(version) VALUES (@v)";
                            var p = ins.CreateParameter();
                            p.ParameterName = "v";
                            p.Value = version;
                            ins.Parameters.Add(p);
                            ins.ExecuteNonQuery();
                        }
                        tx.Commit();
                        count++;
                    }
                    catch
                    {
                        tx.Rollback();
                        throw; // a half-applied schema is worse than a loud failure
                    }
                }
                return count;
            }
            finally
            {
                Exec(conn, null, $"SELECT pg_advisory_unlock({AdvisoryLockKey})");
            }
        }

        private static void Exec(DbConnection conn, DbTransaction? tx, string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.CommandTimeout = 120;
            cmd.ExecuteNonQuery();
        }
    }
}
