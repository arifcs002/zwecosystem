using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Api.Infrastructure
{
    public record OfferDef(int Id, string Name, string Type, int? ProductId, int BuyQty, int GetQty, int MinQty, decimal Percent, decimal BundlePrice, List<(int ProductId, int Qty)> Items);
    public record CartLine(int ProductId, int Qty, decimal Price);
    public record OfferResult(decimal Discount, List<string> Labels);

    // Works out the automatic offer discount for a cart. Pure and deterministic: offers are applied in id order and each
    // unit of a product can be used by only ONE offer, so two offers never discount the same item twice.
    public static class OfferEngine
    {
        public static OfferResult Compute(IEnumerable<OfferDef> offers, IEnumerable<CartLine> lines)
        {
            var rem = new Dictionary<int, int>();
            var spend = new Dictionary<int, decimal>();
            foreach (var l in lines.Where(l => l.Qty > 0))
            {
                rem[l.ProductId] = rem.GetValueOrDefault(l.ProductId) + l.Qty;
                spend[l.ProductId] = spend.GetValueOrDefault(l.ProductId) + l.Qty * l.Price;
            }
            var total0 = spend.Values.Sum();
            decimal Price(int pid) { var q = lines.Where(l => l.ProductId == pid).Sum(l => l.Qty); return q > 0 ? spend[pid] / q : 0m; }

            decimal discount = 0; var labels = new List<string>();
            foreach (var o in offers.OrderBy(o => o.Id))
            {
                decimal d = 0;
                switch (o.Type)
                {
                    case "BUY_GET" when o.ProductId is int p && o.BuyQty >= 1 && o.GetQty >= 1:
                    {
                        var groups = rem.GetValueOrDefault(p) / (o.BuyQty + o.GetQty);
                        if (groups > 0) { d = groups * o.GetQty * Price(p); rem[p] -= groups * (o.BuyQty + o.GetQty); }
                        break;
                    }
                    case "BUNDLE" when o.Items.Count >= 2 && o.BundlePrice > 0:
                    {
                        var k = o.Items.Min(i => rem.GetValueOrDefault(i.ProductId) / Math.Max(i.Qty, 1));
                        if (k > 0)
                        {
                            var regular = o.Items.Sum(i => i.Qty * Price(i.ProductId));
                            d = k * Math.Max(regular - o.BundlePrice, 0);
                            if (d > 0) foreach (var i in o.Items) rem[i.ProductId] -= k * i.Qty;
                        }
                        break;
                    }
                    case "QTY_PERCENT" when o.MinQty >= 2 && o.Percent > 0:
                    {
                        foreach (var pid in rem.Keys.ToList())
                        {
                            if (o.ProductId != null && o.ProductId != pid) continue;
                            if (rem[pid] < o.MinQty) continue;
                            d += Math.Round(rem[pid] * Price(pid) * o.Percent / 100m, 2, MidpointRounding.AwayFromZero);
                            rem[pid] = 0;
                        }
                        break;
                    }
                }
                if (d > 0) { discount += d; labels.Add(o.Name); }
            }
            discount = Math.Round(Math.Min(discount, total0), 2, MidpointRounding.AwayFromZero);
            return new OfferResult(discount, labels);
        }

        private sealed class OfferRow { public int Id { get; set; } public string Name { get; set; } = ""; public string Type { get; set; } = ""; public int? ProductId { get; set; } public int BuyQty { get; set; } public int GetQty { get; set; } public int MinQty { get; set; } public decimal Percent { get; set; } public decimal BundlePrice { get; set; } }
        private sealed class ItemRow { public int OfferId { get; set; } public int ProductId { get; set; } public int Quantity { get; set; } }

        // Offers that are switched on and inside their dates right now.
        public static async Task<List<OfferDef>> LoadActiveAsync(ApplicationDbContext db, int companyId, DateTime? now = null)
        {
            var at = now ?? DateTime.UtcNow;
            var rows = await db.Database.SqlQueryRaw<OfferRow>(
                @"SELECT id AS ""Id"", name AS ""Name"", offer_type AS ""Type"", product_id AS ""ProductId"", buy_qty AS ""BuyQty"", get_qty AS ""GetQty"", min_qty AS ""MinQty"", percent AS ""Percent"", bundle_price AS ""BundlePrice""
                  FROM offers WHERE company_id={0} AND is_active AND (starts_at IS NULL OR starts_at <= {1}) AND (ends_at IS NULL OR ends_at >= {1})", companyId, at).ToListAsync();
            if (rows.Count == 0) return new();
            var ids = rows.Select(r => r.Id).ToArray();
            var items = await db.Database.SqlQueryRaw<ItemRow>(@"SELECT offer_id AS ""OfferId"", product_id AS ""ProductId"", quantity AS ""Quantity"" FROM offer_items WHERE offer_id = ANY({0})", ids).ToListAsync();
            return rows.Select(r => new OfferDef(r.Id, r.Name, r.Type, r.ProductId, r.BuyQty, r.GetQty, r.MinQty, r.Percent, r.BundlePrice,
                items.Where(i => i.OfferId == r.Id).Select(i => (i.ProductId, i.Quantity)).ToList())).ToList();
        }
    }
}
