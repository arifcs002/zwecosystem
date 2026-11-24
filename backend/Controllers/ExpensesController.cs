using System.Globalization;
using Ecommerce.Api.Domain;
using Ecommerce.Api.Infrastructure;
using Ecommerce.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ExpensesController : ControllerBase
    {
        public static readonly string[] DefaultCategories =
            { "Rent", "Salary", "Advertising", "Courier & Delivery", "Packaging", "Utilities", "Transport", "Inventory purchase", "Other" };

        private readonly ApplicationDbContext _context;
        public ExpensesController(ApplicationDbContext context) { _context = context; }

        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? category)
        {
            var q = _context.Expenses.AsNoTracking().AsQueryable();
            if (TryDate(from, out var f)) q = q.Where(e => e.ExpenseDate >= f);
            if (TryDate(to, out var t)) q = q.Where(e => e.ExpenseDate <= t);
            if (!string.IsNullOrWhiteSpace(category)) q = q.Where(e => e.Category == category);

            var rows = await q.OrderByDescending(e => e.ExpenseDate).ThenByDescending(e => e.Id).Take(1000)
                .Select(e => new { e.Id, e.Category, e.Amount, expenseDate = e.ExpenseDate.ToString("yyyy-MM-dd"), e.Payee, e.Note })
                .ToListAsync();
            return Ok(rows);
        }

        [HttpGet("categories")]
        public async Task<IActionResult> Categories()
        {
            var used = await _context.Expenses.AsNoTracking().Select(e => e.Category).Distinct().ToListAsync();
            return Ok(DefaultCategories.Concat(used).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c).ToList());
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] ExpenseUpsertDto dto)
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest(new { message = "Company context is required." });
            var error = Check(dto);
            if (error != null) return BadRequest(new { message = error });

            var e = new Expense { CompanyId = companyId.Value, CreatedBy = _context.CurrentUserId };
            Apply(e, dto);
            _context.Expenses.Add(e);
            await _context.SaveChangesAsync();
            await Audit.LogAsync(_context, HttpContext, "expense.create", $"{e.Category} {e.Amount}");
            return Ok(new { e.Id });
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] ExpenseUpsertDto dto)
        {
            var e = await _context.Expenses.FirstOrDefaultAsync(x => x.Id == id);
            if (e == null) return NotFound();
            var error = Check(dto);
            if (error != null) return BadRequest(new { message = error });
            Apply(e, dto);
            e.UpdatedBy = _context.CurrentUserId;
            e.UpdatedDate = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return Ok(new { e.Id });
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var e = await _context.Expenses.FirstOrDefaultAsync(x => x.Id == id);
            if (e == null) return NotFound();
            e.IsDeleted = 1;
            e.DeletedDate = DateTime.UtcNow;
            e.DeletedBy = _context.CurrentUserId;
            await _context.SaveChangesAsync();
            await Audit.LogAsync(_context, HttpContext, "expense.delete", $"{e.Category} {e.Amount} on {e.ExpenseDate:yyyy-MM-dd}");
            return Ok(new { message = "Expense deleted." });
        }

        internal static bool TryDate(string? s, out DateTime d) =>
            DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d);

        private static string? Check(ExpenseUpsertDto dto)
        {
            var cat = (dto.Category ?? "").Trim();
            if (cat.Length == 0 || cat.Length > 60) return "Category is required (max 60 characters).";
            if (dto.Amount <= 0 || dto.Amount > 1_000_000_000m) return "Amount must be greater than 0.";
            if (dto.ExpenseDate.Year < 2000 || dto.ExpenseDate.Date > DateTime.UtcNow.AddDays(2).Date) return "Enter a valid date (not in the future).";
            if ((dto.Note?.Length ?? 0) > 500 || (dto.Payee?.Length ?? 0) > 120) return "Note or payee is too long.";
            return null;
        }

        private static void Apply(Expense e, ExpenseUpsertDto dto)
        {
            e.Category = dto.Category.Trim();
            e.Amount = Math.Round(dto.Amount, 2);
            e.ExpenseDate = dto.ExpenseDate.Date;
            e.Payee = string.IsNullOrWhiteSpace(dto.Payee) ? null : dto.Payee.Trim();
            e.Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
        }
    }

    // Profit & loss for a date range. Dates are Bangladesh calendar days (UTC+6): an order
    // placed at 11pm in Dhaka counts on that day, not the next UTC day.
    [ApiController]
    [Route("api/reports")]
    [Authorize]
    public class ProfitReportController : ControllerBase
    {
        private const int BdOffsetHours = 6;
        private readonly ApplicationDbContext _context;
        public ProfitReportController(ApplicationDbContext context) { _context = context; }

        [HttpGet("profit")]
        public async Task<IActionResult> Profit([FromQuery] string? from, [FromQuery] string? to)
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest(new { message = "Company context is required." });
            if (!ExpensesController.TryDate(from, out var f) || !ExpensesController.TryDate(to, out var t))
                return BadRequest(new { message = "from and to must be dates like 2026-11-01." });
            if (t < f) return BadRequest(new { message = "'to' must not be before 'from'." });
            if ((t - f).TotalDays > 366) return BadRequest(new { message = "Choose a range of at most one year." });

            var startUtc = DateTime.SpecifyKind(f.Date.AddHours(-BdOffsetHours), DateTimeKind.Utc);
            var endUtc = DateTime.SpecifyKind(t.Date.AddDays(1).AddHours(-BdOffsetHours), DateTimeKind.Utc);

            var orders = await _context.Orders.AsNoTracking()
                .Where(o => o.Status == "COMPLETED" && o.CreatedDate >= startUtc && o.CreatedDate < endUtc)
                .Select(o => new { o.Id, o.Total, o.Discount, o.ShippingFee, o.CreatedDate })
                .ToListAsync();
            var cancelled = await _context.Orders.AsNoTracking()
                .CountAsync(o => o.Status == "CANCELLED" && o.CreatedDate >= startUtc && o.CreatedDate < endUtc);

            var orderIds = orders.Select(o => o.Id).ToList();
            var lines = orderIds.Count == 0
                ? new List<(int OrderId, int ProductId, int Quantity)>()
                : (await _context.OrderItems.AsNoTracking().Where(i => orderIds.Contains(i.OrderId))
                    .Select(i => new { i.OrderId, i.ProductId, i.Quantity }).ToListAsync())
                    .Select(i => (i.OrderId, i.ProductId, i.Quantity)).ToList();

            // Cost comes from the product's wholesale price today (orders don't snapshot cost).
            // IgnoreQueryFilters so a since-deleted product still counts toward cost of goods.
            var productIds = lines.Select(l => l.ProductId).Distinct().ToList();
            var cost = await _context.Products.IgnoreQueryFilters().AsNoTracking()
                .Where(p => p.CompanyId == companyId.Value && productIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.WholesalePrice);

            decimal CogsOf(int orderId) => lines.Where(l => l.OrderId == orderId)
                .Sum(l => l.Quantity * (cost.TryGetValue(l.ProductId, out var c) ? c : 0m));

            var expenses = await _context.Expenses.AsNoTracking()
                .Where(e => e.ExpenseDate >= f.Date && e.ExpenseDate <= t.Date)
                .Select(e => new { e.Category, e.Amount, e.ExpenseDate }).ToListAsync();

            // Returns: the refund value comes off revenue, and goods put back on the shelf come off cost of goods.
            var returns = await _context.OrderReturns.AsNoTracking()
                .Where(r => r.CreatedDate >= startUtc && r.CreatedDate < endUtc)
                .Select(r => new { r.RefundAmount, r.RestockedCost, r.CreatedDate }).ToListAsync();
            var returnedValue = returns.Sum(r => r.RefundAmount);
            var revenue = orders.Sum(o => o.Total) - returnedValue;
            var cogs = orders.Sum(o => CogsOf(o.Id)) - returns.Sum(r => r.RestockedCost);
            var expenseTotal = expenses.Sum(e => e.Amount);
            var gross = revenue - cogs;

            var daily = new List<object>();
            for (var d = f.Date; d <= t.Date; d = d.AddDays(1))
            {
                var dayOrders = orders.Where(o => o.CreatedDate.AddHours(BdOffsetHours).Date == d).ToList();
                var dayReturns = returns.Where(r => r.CreatedDate.AddHours(BdOffsetHours).Date == d).ToList();
                var dayRevenue = dayOrders.Sum(o => o.Total) - dayReturns.Sum(r => r.RefundAmount);
                var dayCogs = dayOrders.Sum(o => CogsOf(o.Id)) - dayReturns.Sum(r => r.RestockedCost);
                var dayExpenses = expenses.Where(e => e.ExpenseDate.Date == d).Sum(e => e.Amount);
                daily.Add(new
                {
                    date = d.ToString("yyyy-MM-dd"), orders = dayOrders.Count,
                    revenue = dayRevenue, cogs = dayCogs, expenses = dayExpenses,
                    profit = dayRevenue - dayCogs - dayExpenses
                });
            }

            return Ok(new
            {
                from = f.ToString("yyyy-MM-dd"), to = t.ToString("yyyy-MM-dd"),
                note = "Revenue counts COMPLETED orders. Cost of goods uses each product's current wholesale price.",
                summary = new
                {
                    orders = orders.Count, cancelledOrders = cancelled,
                    revenue, returnsValue = returnedValue, discounts = orders.Sum(o => o.Discount), shippingCollected = orders.Sum(o => o.ShippingFee),
                    cogs, grossProfit = gross,
                    grossMarginPercent = revenue > 0 ? Math.Round(gross / revenue * 100, 1) : 0m,
                    expenses = expenseTotal, netProfit = gross - expenseTotal
                },
                expensesByCategory = expenses.GroupBy(e => e.Category)
                    .Select(g => new { category = g.Key, total = g.Sum(x => x.Amount) })
                    .OrderByDescending(x => x.total).ToList(),
                daily
            });
        }
    }
}
