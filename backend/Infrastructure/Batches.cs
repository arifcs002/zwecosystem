using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Api.Infrastructure
{
    // Product lots with expiry dates. Stock itself stays in products.stock_quantity; batches say which part of it expires when.
    public static class Batches
    {
        public static DateTime TodayBd() => DateTime.UtcNow.AddHours(6).Date;
        // Npgsql refuses an unspecified-kind DateTime for a date column; DateOnly is what a date really is.
        public static DateOnly D(this DateTime d) => DateOnly.FromDateTime(d);

        // Units of this product that are past their expiry date and still on the shelf.
        public static async Task<int> ExpiredQtyAsync(ApplicationDbContext db, int companyId, int productId) =>
            (await db.Database.SqlQueryRaw<int>(
                @"SELECT COALESCE(SUM(quantity),0)::int AS ""Value"" FROM product_batches WHERE company_id={0} AND product_id={1} AND status='ACTIVE' AND quantity > 0 AND expiry_date < {2}::date",
                companyId, productId, TodayBd().D()).ToListAsync()).First();

        private sealed class BatchQty { public int Id { get; set; } public int Quantity { get; set; } }

        // First-expiring-first-out: a sale takes units from the lot that expires soonest, and never touches expired lots.
        public static async Task ConsumeAsync(ApplicationDbContext db, int companyId, int productId, int qty)
        {
            if (qty <= 0) return;
            var lots = await db.Database.SqlQueryRaw<BatchQty>(
                @"SELECT id AS ""Id"", quantity AS ""Quantity"" FROM product_batches WHERE company_id={0} AND product_id={1} AND status='ACTIVE' AND quantity > 0 AND expiry_date >= {2}::date ORDER BY expiry_date, id",
                companyId, productId, TodayBd().D()).ToListAsync();
            foreach (var lot in lots)
            {
                if (qty <= 0) break;
                var take = Math.Min(lot.Quantity, qty);
                await db.Database.ExecuteSqlRawAsync("UPDATE product_batches SET quantity = GREATEST(quantity - {0}, 0) WHERE id = {1}", take, lot.Id);
                qty -= take;
            }
        }
    }
}
