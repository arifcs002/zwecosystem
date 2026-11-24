using System.Globalization;
using Ecommerce.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Api.Controllers
{
    // Owner-only business insight: which products really earn money, which stock is stuck, who buys and how they pay.
    // Cost comes from each product's CURRENT wholesale price (orders don't snapshot cost), same as the profit report.
    [ApiController]
    [Route("api/analytics")]
    [Authorize]
    public class AnalyticsController : ControllerBase
    {
        private const int BdOffsetHours = 6;
        private readonly ApplicationDbContext _context;
        public AnalyticsController(ApplicationDbContext context) { _context = context; }

        public sealed class ProductRow { public int ProductId { get; set; } public string Name { get; set; } = ""; public int Units { get; set; } public int ReturnedUnits { get; set; } public decimal Revenue { get; set; } public decimal Cost { get; set; } public decimal Profit { get; set; } public decimal MarginPercent { get; set; } }
        public sealed class DeadRow { public int ProductId { get; set; } public string Name { get; set; } = ""; public int Stock { get; set; } public decimal CostValue { get; set; } public DateTime? LastSold { get; set; } public int DaysSinceSale { get; set; } }
        public sealed class GroupRow { public string Key { get; set; } = ""; public int Orders { get; set; } public decimal Revenue { get; set; } }
        public sealed class CustomerRow { public string Phone { get; set; } = ""; public string? Name { get; set; } public int Orders { get; set; } public decimal Spent { get; set; } }
        public sealed class Totals { public int Orders { get; set; } public decimal Revenue { get; set; } public int Customers { get; set; } public int RepeatCustomers { get; set; } }

        private static bool TryRange(string? from, string? to, out DateTime s, out DateTime e, out string? err)
        {
            s = e = default; err = null;
            if (!DateTime.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var f) ||
                !DateTime.TryParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) { err = "from and to must be dates like 2026-11-01."; return false; }
            if (t < f) { err = "'to' must not be before 'from'."; return false; }
            if ((t - f).TotalDays > 366) { err = "Choose a range of at most one year."; return false; }
            s = DateTime.SpecifyKind(f.Date.AddHours(-BdOffsetHours), DateTimeKind.Utc);
            e = DateTime.SpecifyKind(t.Date.AddDays(1).AddHours(-BdOffsetHours), DateTimeKind.Utc);
            return true;
        }

        // Shared with the CSV export.
        [NonAction]
        public async Task<List<ProductRow>> ProductsAsync(int companyId, DateTime s, DateTime e, int limit) =>
            await _context.Database.SqlQueryRaw<ProductRow>(
                @"WITH lines AS (
                    SELECT oi.product_id, SUM(oi.quantity)::int AS qty,
                           SUM(oi.price * oi.quantity * (CASE WHEN o.subtotal > 0 THEN GREATEST(1 - o.discount / o.subtotal, 0) ELSE 1 END)) AS rev
                    FROM order_items oi JOIN orders o ON o.id = oi.order_id
                    WHERE o.company_id = {0} AND o.status = 'COMPLETED' AND o.created_date >= {1} AND o.created_date < {2}
                    GROUP BY oi.product_id),
                  rets AS (
                    SELECT ri.product_id, SUM(ri.quantity)::int AS qty, SUM(ri.quantity * ri.unit_price) AS val
                    FROM order_return_items ri JOIN order_returns r ON r.id = ri.return_id
                    WHERE r.company_id = {0} AND r.created_date >= {1} AND r.created_date < {2}
                    GROUP BY ri.product_id)
                  SELECT p.id AS ""ProductId"", p.name AS ""Name"", GREATEST(l.qty - COALESCE(r.qty,0), 0) AS ""Units"", COALESCE(r.qty,0) AS ""ReturnedUnits"",
                         ROUND(l.rev - COALESCE(r.val,0), 2) AS ""Revenue"",
                         ROUND(GREATEST(l.qty - COALESCE(r.qty,0), 0) * p.wholesale_price, 2) AS ""Cost"",
                         ROUND((l.rev - COALESCE(r.val,0)) - GREATEST(l.qty - COALESCE(r.qty,0), 0) * p.wholesale_price, 2) AS ""Profit"",
                         CASE WHEN (l.rev - COALESCE(r.val,0)) > 0 THEN ROUND((((l.rev - COALESCE(r.val,0)) - GREATEST(l.qty - COALESCE(r.qty,0), 0) * p.wholesale_price) / (l.rev - COALESCE(r.val,0))) * 100, 1) ELSE 0 END AS ""MarginPercent""
                  FROM lines l JOIN products p ON p.id = l.product_id AND p.company_id = {0}
                  LEFT JOIN rets r ON r.product_id = l.product_id
                  ORDER BY (l.rev - COALESCE(r.val,0)) - GREATEST(l.qty - COALESCE(r.qty,0), 0) * p.wholesale_price DESC
                  LIMIT {3}", companyId, s, e, limit).ToListAsync();

        // Products that have stock but have not sold for `days` days (and are older than that).
        [NonAction]
        public async Task<List<DeadRow>> DeadStockAsync(int companyId, int days) =>
            await _context.Database.SqlQueryRaw<DeadRow>(
                @"SELECT p.id AS ""ProductId"", p.name AS ""Name"", p.stock_quantity AS ""Stock"", ROUND(p.stock_quantity * p.wholesale_price, 2) AS ""CostValue"",
                         s.last_sold AS ""LastSold"", COALESCE(EXTRACT(DAY FROM NOW() - s.last_sold)::int, EXTRACT(DAY FROM NOW() - p.created_date)::int) AS ""DaysSinceSale""
                  FROM products p
                  LEFT JOIN (SELECT oi.product_id, MAX(o.created_date) AS last_sold FROM order_items oi JOIN orders o ON o.id = oi.order_id
                             WHERE o.company_id = {0} AND o.status <> 'CANCELLED' GROUP BY oi.product_id) s ON s.product_id = p.id
                  WHERE p.company_id = {0} AND p.is_deleted = 0 AND p.stock_quantity > 0 AND p.created_date < NOW() - make_interval(days => {1})
                    AND (s.last_sold IS NULL OR s.last_sold < NOW() - make_interval(days => {1}))
                  ORDER BY p.stock_quantity * p.wholesale_price DESC LIMIT 500", companyId, days).ToListAsync();

        [HttpGet("products")]
        public async Task<IActionResult> Products([FromQuery] string? from, [FromQuery] string? to, [FromQuery] int limit = 50)
        {
            var cid = _context.CompanyId;
            if (!cid.HasValue) return BadRequest(new { message = "Company context is required." });
            if (!TryRange(from, to, out var s, out var e, out var err)) return BadRequest(new { message = err });
            return Ok(await ProductsAsync(cid.Value, s, e, Math.Clamp(limit, 1, 500)));
        }

        [HttpGet("dead-stock")]
        public async Task<IActionResult> DeadStock([FromQuery] int days = 60)
        {
            var cid = _context.CompanyId;
            if (!cid.HasValue) return BadRequest(new { message = "Company context is required." });
            var rows = await DeadStockAsync(cid.Value, Math.Clamp(days, 14, 730));
            return Ok(new { days = Math.Clamp(days, 14, 730), totalCost = rows.Sum(r => r.CostValue), count = rows.Count, items = rows });
        }

        [HttpGet("summary")]
        public async Task<IActionResult> Summary([FromQuery] string? from, [FromQuery] string? to)
        {
            var cid = _context.CompanyId;
            if (!cid.HasValue) return BadRequest(new { message = "Company context is required." });
            if (!TryRange(from, to, out var s, out var e, out var err)) return BadRequest(new { message = err });
            const string inRange = "company_id = {0} AND status = 'COMPLETED' AND created_date >= {1} AND created_date < {2}";
            var channels = await _context.Database.SqlQueryRaw<GroupRow>($@"SELECT sale_type AS ""Key"", COUNT(*)::int AS ""Orders"", COALESCE(SUM(total),0) AS ""Revenue"" FROM orders WHERE {inRange} GROUP BY sale_type ORDER BY 3 DESC", cid.Value, s, e).ToListAsync();
            var payments = await _context.Database.SqlQueryRaw<GroupRow>($@"SELECT payment_method AS ""Key"", COUNT(*)::int AS ""Orders"", COALESCE(SUM(total),0) AS ""Revenue"" FROM orders WHERE {inRange} GROUP BY payment_method ORDER BY 3 DESC", cid.Value, s, e).ToListAsync();
            // a repeat customer = a phone number that also bought before this range
            var totals = (await _context.Database.SqlQueryRaw<Totals>(
                $@"SELECT COUNT(*)::int AS ""Orders"", COALESCE(SUM(total),0) AS ""Revenue"",
                          COUNT(DISTINCT customer_phone) FILTER (WHERE COALESCE(customer_phone,'') <> '')::int AS ""Customers"",
                          COUNT(DISTINCT customer_phone) FILTER (WHERE COALESCE(customer_phone,'') <> '' AND EXISTS (SELECT 1 FROM orders p WHERE p.company_id = orders.company_id AND p.customer_phone = orders.customer_phone AND p.status = 'COMPLETED' AND p.created_date < {{1}}))::int AS ""RepeatCustomers""
                   FROM orders WHERE {inRange}", cid.Value, s, e).ToListAsync()).First();
            var top = await _context.Database.SqlQueryRaw<CustomerRow>(
                $@"SELECT customer_phone AS ""Phone"", MAX(customer_name) AS ""Name"", COUNT(*)::int AS ""Orders"", SUM(total) AS ""Spent"" FROM orders
                   WHERE {inRange} AND COALESCE(customer_phone,'') <> '' GROUP BY customer_phone ORDER BY SUM(total) DESC LIMIT 10", cid.Value, s, e).ToListAsync();
            return Ok(new
            {
                orders = totals.Orders, revenue = totals.Revenue, averageOrder = totals.Orders > 0 ? Math.Round(totals.Revenue / totals.Orders, 2) : 0m,
                customers = totals.Customers, repeatCustomers = totals.RepeatCustomers, newCustomers = totals.Customers - totals.RepeatCustomers,
                channels, payments, topCustomers = top
            });
        }
    }
}
