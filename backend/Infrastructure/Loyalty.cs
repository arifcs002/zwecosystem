using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Api.Infrastructure
{
    public record LoyaltyConfig(bool Enabled, decimal EarnPer100, decimal PointValue, int MinRedeem);

    // Points are a second append-only ledger next to the money ledgers. Off by default; the owner chooses the rates.
    public static class Loyalty
    {
        public static async Task<LoyaltyConfig> ConfigAsync(ApplicationDbContext db, int companyId)
        {
            var s = await db.CompanySettings.AsNoTracking().Where(x => x.CompanyId == companyId && x.Key.StartsWith("loyalty_"))
                .ToDictionaryAsync(x => x.Key, x => x.Value);
            decimal D(string k, decimal def) => s.TryGetValue(k, out var v) && decimal.TryParse(v, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d) && d >= 0 ? d : def;
            var enabled = s.TryGetValue("loyalty_enabled", out var e) && (e == "1" || e.Equals("true", StringComparison.OrdinalIgnoreCase));
            return new LoyaltyConfig(enabled, D("loyalty_earn_per_100", 1), D("loyalty_point_value", 1), (int)D("loyalty_min_redeem", 50));
        }

        public static int PointsFor(LoyaltyConfig c, decimal amount) => amount <= 0 ? 0 : (int)Math.Floor(amount / 100m * c.EarnPer100);

        public static Task LockAsync(ApplicationDbContext db, int companyId, string phone) =>
            db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtext({0}))", $"loyalty:{companyId}:{phone}");

        public static async Task<int> BalanceAsync(ApplicationDbContext db, int companyId, string phone) =>
            (await db.Database.SqlQueryRaw<int>("SELECT COALESCE(SUM(points),0)::int AS \"Value\" FROM loyalty_ledger WHERE company_id = {0} AND phone = {1}", companyId, phone).ToListAsync()).First();

        public static Task AddAsync(ApplicationDbContext db, int companyId, string phone, string type, int points, int? orderId, string? note, int? by) =>
            db.Database.ExecuteSqlRawAsync(
                @"INSERT INTO loyalty_ledger (company_id, phone, entry_type, points, order_id, note, recorded_by)
                  VALUES ({0},{1},{2},{3},{4}::int,{5}::text,{6}::int) ON CONFLICT DO NOTHING",
                companyId, phone, type, points, orderId, note, by);

        private sealed record Row(string Phone, string EntryType, int Points);
        private static async Task<List<Row>> RowsAsync(ApplicationDbContext db, int companyId, int orderId) =>
            await db.Database.SqlQueryRaw<Row>(@"SELECT phone AS ""Phone"", entry_type AS ""EntryType"", points AS ""Points"" FROM loyalty_ledger WHERE company_id = {0} AND order_id = {1}", companyId, orderId).ToListAsync();

        // Whole order cancelled: take back what it earned, give back what it spent.
        public static async Task ReverseOnCancelAsync(ApplicationDbContext db, int companyId, int orderId, int? by)
        {
            var rows = await RowsAsync(db, companyId, orderId);
            var earned = rows.Where(r => r.EntryType == "EARN").Sum(r => r.Points);
            var spent = -rows.Where(r => r.EntryType == "REDEEM").Sum(r => r.Points);
            var phone = rows.FirstOrDefault()?.Phone;
            if (phone == null) return;
            await LockAsync(db, companyId, phone);
            if (earned > 0) await AddAsync(db, companyId, phone, "CANCEL_EARN", -earned, orderId, "Order cancelled", by);
            if (spent > 0) await AddAsync(db, companyId, phone, "CANCEL_REDEEM", spent, orderId, "Order cancelled", by);
        }

        // Goods returned: the points they earned go back in proportion to the refunded value (spent points are not returned).
        public static async Task ReverseOnReturnAsync(ApplicationDbContext db, int companyId, int orderId, decimal refund, decimal orderTotal, string returnNumber, int? by)
        {
            if (orderTotal <= 0 || refund <= 0) return;
            var rows = await RowsAsync(db, companyId, orderId);
            var phone = rows.FirstOrDefault()?.Phone;
            if (phone == null) return;
            var earned = rows.Where(r => r.EntryType == "EARN").Sum(r => r.Points);
            var reversed = -rows.Where(r => r.EntryType == "RETURN_REVERSE").Sum(r => r.Points);
            var take = Math.Min(earned - reversed, (int)Math.Ceiling(earned * Math.Min(1m, refund / orderTotal)));
            if (take <= 0) return;
            await LockAsync(db, companyId, phone);
            await AddAsync(db, companyId, phone, "RETURN_REVERSE", -take, orderId, $"Return {returnNumber}", by);
        }
    }
}
