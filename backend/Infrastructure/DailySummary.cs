using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Api.Infrastructure
{
    public sealed class Counts { public decimal Revenue { get; set; } public int Orders { get; set; } public decimal Given { get; set; } public decimal Collected { get; set; } public decimal Returns { get; set; } public decimal Expenses { get; set; } public int LowStock { get; set; } public int OutOfStock { get; set; } public int ExpiringLots { get; set; } public int ExpiredLots { get; set; } public string Shop { get; set; } = ""; }

    // The numbers behind the owner's evening SMS and the dashboard alerts. Plain SQL, so it also works with no signed-in user.
    public static class DailySummary
    {
        public static DateTime BdToday() => DateTime.UtcNow.AddHours(6).Date;

        public static async Task<Counts> CountsAsync(ApplicationDbContext db, int companyId, DateTime bdDay)
        {
            var from = DateTime.SpecifyKind(bdDay.AddHours(-6), DateTimeKind.Utc);
            var to = from.AddDays(1);
            return (await db.Database.SqlQueryRaw<Counts>(
                @"SELECT
                    COALESCE((SELECT SUM(total) FROM orders WHERE company_id={0} AND status <> 'CANCELLED' AND created_date >= {1} AND created_date < {2}),0) AS ""Revenue"",
                    (SELECT COUNT(*) FROM orders WHERE company_id={0} AND status <> 'CANCELLED' AND created_date >= {1} AND created_date < {2})::int AS ""Orders"",
                    COALESCE((SELECT SUM(amount) FROM credit_ledger WHERE company_id={0} AND entry_type='SALE' AND created_date >= {1} AND created_date < {2}),0) AS ""Given"",
                    COALESCE((SELECT -SUM(amount) FROM credit_ledger WHERE company_id={0} AND entry_type='PAYMENT' AND created_date >= {1} AND created_date < {2}),0) AS ""Collected"",
                    COALESCE((SELECT SUM(refund_amount) FROM order_returns WHERE company_id={0} AND created_date >= {1} AND created_date < {2}),0) AS ""Returns"",
                    COALESCE((SELECT SUM(amount) FROM expenses WHERE company_id={0} AND expense_date = {3}::date AND is_deleted = 0),0) AS ""Expenses"",
                    (SELECT COUNT(*) FROM products WHERE company_id={0} AND is_deleted = 0 AND stock_quantity > 0 AND stock_quantity <= 10)::int AS ""LowStock"",
                    (SELECT COUNT(*) FROM products WHERE company_id={0} AND is_deleted = 0 AND stock_quantity = 0)::int AS ""OutOfStock"",
                    (SELECT COUNT(*) FROM product_batches WHERE company_id={0} AND status='ACTIVE' AND quantity > 0 AND expiry_date >= {3}::date AND expiry_date <= {3}::date + 14)::int AS ""ExpiringLots"",
                    (SELECT COUNT(*) FROM product_batches WHERE company_id={0} AND status='ACTIVE' AND quantity > 0 AND expiry_date < {3}::date)::int AS ""ExpiredLots"",
                    COALESCE((SELECT name FROM companies WHERE id={0}),'') AS ""Shop""", companyId, from, to, bdDay.D()).ToListAsync()).First();
        }

        public static string Message(Counts c, DateTime bdDay)
        {
            string M(decimal v) => "৳" + Math.Round(v).ToString("N0", CultureInfo.InvariantCulture);
            var s = $"{c.Shop} {bdDay:d MMM}: Sales {M(c.Revenue)} ({c.Orders} orders). Baki given {M(c.Given)}, collected {M(c.Collected)}. Returns {M(c.Returns)}. Expenses {M(c.Expenses)}.";
            if (c.LowStock + c.OutOfStock > 0) s += $" Low stock {c.LowStock}, out {c.OutOfStock}.";
            if (c.ExpiredLots + c.ExpiringLots > 0) s += $" Expired lots {c.ExpiredLots}, expiring soon {c.ExpiringLots}.";
            return s;
        }
    }

    // Sends each opted-in shop its summary once per evening (after 9pm Bangladesh time). The unique row in
    // daily_summaries is claimed BEFORE sending, so a restart or a second server can never send it twice.
    public sealed class DailySummaryService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<DailySummaryService> _log;
        public DailySummaryService(IServiceScopeFactory scopes, ILogger<DailySummaryService> log) { _scopes = scopes; _log = log; }

        private sealed class Target { public int CompanyId { get; set; } public string Phone { get; set; } = ""; public string Gateway { get; set; } = ""; }

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            while (!ct.IsCancellationRequested)
            {
                try { await RunOnceAsync(DateTime.UtcNow, ct); }
                catch (Exception ex) when (ex is not OperationCanceledException) { _log.LogWarning(ex, "Daily summary run failed"); }
                await Task.Delay(TimeSpan.FromMinutes(5), ct);
            }
        }

        public async Task RunOnceAsync(DateTime utcNow, CancellationToken ct)
        {
            var bd = utcNow.AddHours(6);
            if (bd.Hour < 21) return;
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var sms = scope.ServiceProvider.GetRequiredService<ISmsSender>();
            var targets = await db.Database.SqlQueryRaw<Target>(
                @"SELECT e.company_id AS ""CompanyId"", ph.value AS ""Phone"", g.value AS ""Gateway""
                  FROM company_settings e
                  JOIN company_settings ph ON ph.company_id = e.company_id AND ph.key = 'daily_summary_phone'
                  JOIN company_settings g ON g.company_id = e.company_id AND g.key = 'sms_api_url'
                  WHERE e.key = 'daily_summary_enabled' AND e.value IN ('1','true') AND COALESCE(ph.value,'') <> '' AND COALESCE(g.value,'') <> ''").ToListAsync(ct);
            foreach (var t in targets)
            {
                var phone = PhoneUtil.NormalizeBd(t.Phone);
                if (phone == null) continue;
                var claimed = await db.Database.ExecuteSqlRawAsync("INSERT INTO daily_summaries (company_id, summary_date) VALUES ({0},{1}::date) ON CONFLICT DO NOTHING", t.CompanyId, bd.Date.D());
                if (claimed == 0) continue;
                var counts = await DailySummary.CountsAsync(db, t.CompanyId, bd.Date);
                await sms.SendAsync(t.Gateway, phone, DailySummary.Message(counts, bd.Date));
            }
        }
    }
}
