using Ecommerce.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Api.Controllers
{
    // "What should I buy from my suppliers?" Looks at how fast each product really sold lately, what is on the shelf
    // and what is already on order, and lists what will run out soon with a suggested quantity. It only suggests:
    // the manager turns it into a purchase order on the Purchases page.
    [ApiController]
    [Route("api/reorder")]
    [Authorize]
    public class ReorderController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        public ReorderController(ApplicationDbContext context) { _context = context; }

        public sealed class Row
        {
            public int ProductId { get; set; } public string Name { get; set; } = ""; public int? SupplierId { get; set; } public string? SupplierName { get; set; }
            public int Stock { get; set; } public int Sold { get; set; } public int OnOrder { get; set; } public decimal UnitCost { get; set; }
        }

        // windowDays: how far back to measure sales. coverDays: how many days of sales a fresh order should last.
        // warnDays: suggest when stock (plus what is already ordered) covers fewer days than this.
        [HttpGet("suggestions")]
        public async Task<IActionResult> Suggestions([FromQuery] int windowDays = 30, [FromQuery] int coverDays = 30, [FromQuery] int warnDays = 10)
        {
            var cid = _context.CompanyId;
            if (!cid.HasValue) return BadRequest(new { message = "Company context is required." });
            windowDays = Math.Clamp(windowDays, 7, 180); coverDays = Math.Clamp(coverDays, 7, 180); warnDays = Math.Clamp(warnDays, 1, 90);
            var since = DateTime.UtcNow.AddDays(-windowDays);
            var rows = await _context.Database.SqlQueryRaw<Row>(
                @"SELECT p.id AS ""ProductId"", p.name AS ""Name"", p.supplier_id AS ""SupplierId"", s.name AS ""SupplierName"", p.stock_quantity AS ""Stock"", p.wholesale_price AS ""UnitCost"",
                         COALESCE((SELECT SUM(oi.quantity) FROM order_items oi JOIN orders o ON o.id = oi.order_id
                                   WHERE o.company_id = p.company_id AND oi.product_id = p.id AND o.status <> 'CANCELLED' AND o.created_date >= {1}), 0)::int AS ""Sold"",
                         COALESCE((SELECT SUM(GREATEST(i.quantity - i.received_qty, 0)) FROM purchase_order_items i JOIN purchase_orders po ON po.id = i.purchase_order_id
                                   WHERE po.company_id = p.company_id AND i.product_id = p.id AND po.status IN ('ORDERED','PARTIAL')), 0)::int AS ""OnOrder""
                  FROM products p LEFT JOIN suppliers s ON s.id = p.supplier_id
                  WHERE p.company_id = {0} AND p.is_deleted = 0", cid.Value, since).ToListAsync();

            var list = rows.Where(r => r.Sold > 0).Select(r =>
            {
                var perDay = (decimal)r.Sold / windowDays;
                var coverNow = (r.Stock + r.OnOrder) / perDay;                      // days the stock + incoming will last
                var want = (int)Math.Ceiling(perDay * coverDays) - r.Stock - r.OnOrder;
                return new
                {
                    r.ProductId, r.Name, r.SupplierId, r.SupplierName, r.Stock, r.OnOrder, r.Sold, r.UnitCost,
                    perDay = Math.Round(perDay, 2), daysLeft = Math.Round(r.Stock / perDay, 1), coverDays = Math.Round(coverNow, 1),
                    suggestedQty = Math.Max(want, 0), suggestedCost = Math.Max(want, 0) * r.UnitCost
                };
            }).Where(x => x.coverDays < warnDays && x.suggestedQty > 0).OrderBy(x => x.coverDays).ToList();

            return Ok(new
            {
                windowDays, coverDays, warnDays, total = list.Count, totalCost = list.Sum(x => x.suggestedCost),
                items = list,
                bySupplier = list.GroupBy(x => new { x.SupplierId, x.SupplierName }).Select(g => new { supplierId = g.Key.SupplierId, supplierName = g.Key.SupplierName ?? "No supplier set", count = g.Count(), cost = g.Sum(x => x.suggestedCost) })
            });
        }
    }
}
