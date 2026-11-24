using System.Globalization;
using System.Text;
using Ecommerce.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Api.Controllers
{
    // Spreadsheet-friendly CSV downloads of the main reports. Everything is scoped to the signed-in shop by the
    // normal tenant query filters. The files start with a UTF-8 BOM so Excel shows Bangla names correctly.
    [ApiController]
    [Route("api/reports/export")]
    [Authorize]
    public class ReportExportController : ControllerBase
    {
        private const int BdOffsetHours = 6;
        private const int MaxRows = 50000;
        private readonly ApplicationDbContext _context;
        public ReportExportController(ApplicationDbContext context) { _context = context; }

        // A cell that starts with = + - @ is treated as a formula by Excel; a customer name like "=HYPERLINK(...)" could run
        // on the owner's machine. Prefix such text with an apostrophe (numbers are written separately and untouched).
        private static string Cell(object? v)
        {
            string s = v switch
            {
                null => "",
                decimal d => d.ToString("0.##", CultureInfo.InvariantCulture),
                double d => d.ToString("0.##", CultureInfo.InvariantCulture),
                DateTime dt => dt.AddHours(BdOffsetHours).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                bool b => b ? "yes" : "no",
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => v.ToString() ?? ""
            };
            if (v is string && s.Length > 0 && "=+-@\t\r".Contains(s[0])) s = "'" + s;
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0) s = "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        private FileContentResult Csv(string name, string[] header, IEnumerable<object?[]> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", header.Select(h => Cell(h))));
            foreach (var r in rows) sb.AppendLine(string.Join(",", r.Select(Cell)));
            var bytes = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
            return File(bytes, "text/csv; charset=utf-8", $"{name}-{DateTime.UtcNow.AddHours(BdOffsetHours):yyyyMMdd}.csv");
        }

        private bool Range(string? from, string? to, out DateTime startUtc, out DateTime endUtc, out string? error)
        {
            startUtc = endUtc = default; error = null;
            if (!ExpensesController.TryDate(from, out var f) || !ExpensesController.TryDate(to, out var t)) { error = "from and to must be dates like 2026-11-01."; return false; }
            if (t < f) { error = "'to' must not be before 'from'."; return false; }
            if ((t - f).TotalDays > 366) { error = "Choose a range of at most one year."; return false; }
            startUtc = DateTime.SpecifyKind(f.Date.AddHours(-BdOffsetHours), DateTimeKind.Utc);
            endUtc = DateTime.SpecifyKind(t.Date.AddDays(1).AddHours(-BdOffsetHours), DateTimeKind.Utc);
            return true;
        }

        [HttpGet("sales")]
        public async Task<IActionResult> Sales([FromQuery] string? from, [FromQuery] string? to)
        {
            if (!_context.CompanyId.HasValue) return BadRequest(new { message = "Company context is required." });
            if (!Range(from, to, out var s, out var e, out var err)) return BadRequest(new { message = err });
            var rows = await _context.Orders.AsNoTracking().Where(o => o.CreatedDate >= s && o.CreatedDate < e)
                .OrderBy(o => o.CreatedDate).Take(MaxRows)
                .Select(o => new { o.OrderNumber, o.CreatedDate, o.SaleType, o.Status, o.CustomerName, o.CustomerPhone, o.Subtotal, o.Discount, o.ShippingFee, o.Total, o.PaymentMethod, o.PaymentStatus })
                .ToListAsync();
            return Csv("sales", new[] { "Order", "Date (BD time)", "Channel", "Status", "Customer", "Phone", "Subtotal", "Discount", "Shipping", "Total", "Payment method", "Payment status" },
                rows.Select(o => new object?[] { o.OrderNumber, o.CreatedDate, o.SaleType, o.Status, o.CustomerName, o.CustomerPhone, o.Subtotal, o.Discount, o.ShippingFee, o.Total, o.PaymentMethod, o.PaymentStatus }));
        }

        [HttpGet("profit")]
        public async Task<IActionResult> Profit([FromQuery] string? from, [FromQuery] string? to)
        {
            var res = await new ProfitReportController(_context).Profit(from, to);
            if (res is not OkObjectResult ok) return res;
            var json = System.Text.Json.JsonSerializer.SerializeToElement(ok.Value);
            var daily = json.GetProperty("daily").EnumerateArray().Select(d => new object?[] {
                d.GetProperty("date").GetString(), d.GetProperty("orders").GetInt32(), d.GetProperty("revenue").GetDecimal(),
                d.GetProperty("cogs").GetDecimal(), d.GetProperty("expenses").GetDecimal(), d.GetProperty("profit").GetDecimal() });
            return Csv("profit-by-day", new[] { "Date", "Orders", "Revenue", "Cost of goods", "Expenses", "Profit" }, daily);
        }

        [HttpGet("expenses")]
        public async Task<IActionResult> Expenses([FromQuery] string? from, [FromQuery] string? to)
        {
            if (!_context.CompanyId.HasValue) return BadRequest(new { message = "Company context is required." });
            var q = _context.Expenses.AsNoTracking().AsQueryable();
            if (ExpensesController.TryDate(from, out var f)) q = q.Where(x => x.ExpenseDate >= f);
            if (ExpensesController.TryDate(to, out var t)) q = q.Where(x => x.ExpenseDate <= t);
            var rows = await q.OrderBy(x => x.ExpenseDate).Take(MaxRows).Select(x => new { x.ExpenseDate, x.Category, x.Amount, x.Payee, x.Note }).ToListAsync();
            return Csv("expenses", new[] { "Date", "Category", "Amount", "Paid to", "Note" },
                rows.Select(x => new object?[] { x.ExpenseDate.ToString("yyyy-MM-dd"), x.Category, x.Amount, x.Payee, x.Note }));
        }

        // Who owes the shop money (baki), biggest first.
        [HttpGet("dues")]
        public async Task<IActionResult> Dues()
        {
            var cid = _context.CompanyId;
            if (!cid.HasValue) return BadRequest(new { message = "Company context is required." });
            var balances = (await _context.CreditLedger.AsNoTracking().Where(x => x.CompanyId == cid.Value)
                .GroupBy(x => x.Phone).Select(g => new { Phone = g.Key, Balance = g.Sum(x => x.Amount), Last = g.Max(x => x.CreatedDate) }).ToListAsync())
                .Where(b => b.Balance > 0).OrderByDescending(b => b.Balance).ToList();
            var phones = balances.Select(b => b.Phone).ToList();
            var names = (await _context.Orders.AsNoTracking().Where(o => o.CustomerPhone != null && phones.Contains(o.CustomerPhone) && o.CustomerName != null)
                .OrderByDescending(o => o.CreatedDate).Select(o => new { o.CustomerPhone, o.CustomerName }).ToListAsync())
                .GroupBy(o => o.CustomerPhone!).ToDictionary(g => g.Key, g => g.First().CustomerName);
            return Csv("customer-dues", new[] { "Phone", "Name", "Owes (BDT)", "Last activity" },
                balances.Select(b => new object?[] { b.Phone, names.GetValueOrDefault(b.Phone), b.Balance, b.Last }));
        }

        [HttpGet("supplier-dues")]
        public async Task<IActionResult> SupplierDues()
        {
            var cid = _context.CompanyId;
            if (!cid.HasValue) return BadRequest(new { message = "Company context is required." });
            var balances = (await _context.SupplierLedger.AsNoTracking().Where(x => x.CompanyId == cid.Value)
                .GroupBy(x => x.SupplierId).Select(g => new { Id = g.Key, Balance = g.Sum(x => x.Amount) }).ToListAsync())
                .Where(b => b.Balance > 0).OrderByDescending(b => b.Balance).ToList();
            var ids = balances.Select(b => b.Id).ToList();
            var sup = await _context.Suppliers.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == cid.Value && ids.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => new { x.Name, x.PhoneNumber });
            return Csv("supplier-dues", new[] { "Supplier", "Phone", "We owe (BDT)" },
                balances.Select(b => new object?[] { sup.GetValueOrDefault(b.Id)?.Name, sup.GetValueOrDefault(b.Id)?.PhoneNumber, b.Balance }));
        }

        [HttpGet("returns")]
        public async Task<IActionResult> Returns([FromQuery] string? from, [FromQuery] string? to)
        {
            if (!_context.CompanyId.HasValue) return BadRequest(new { message = "Company context is required." });
            if (!Range(from, to, out var s, out var e, out var err)) return BadRequest(new { message = err });
            var rows = await _context.OrderReturns.AsNoTracking().Where(r => r.CreatedDate >= s && r.CreatedDate < e)
                .OrderBy(r => r.CreatedDate).Take(MaxRows)
                .Select(r => new { r.ReturnNumber, r.CreatedDate, r.Kind, r.OrderId, r.RefundAmount, r.RefundMethod, r.CashRefunded, r.Reason }).ToListAsync();
            return Csv("returns", new[] { "Return", "Date (BD time)", "Type", "Order id", "Goods value", "Settled by", "Cash refunded", "Reason" },
                rows.Select(r => new object?[] { r.ReturnNumber, r.CreatedDate, r.Kind, r.OrderId, r.RefundAmount, r.RefundMethod, r.CashRefunded, r.Reason }));
        }

        // Shows cost prices, so it is an owner-only report (see AccessPolicy).
        [HttpGet("stock")]
        public async Task<IActionResult> Stock()
        {
            if (!_context.CompanyId.HasValue) return BadRequest(new { message = "Company context is required." });
            var rows = await _context.Products.AsNoTracking().OrderBy(p => p.Name).Take(MaxRows)
                .Select(p => new { p.Name, p.Sku, p.Barcode, p.StockQuantity, p.WholesalePrice, p.Price }).ToListAsync();
            return Csv("stock", new[] { "Product", "SKU", "Barcode", "In stock", "Cost price", "Selling price", "Stock value at cost", "Stock value at selling price" },
                rows.Select(p => new object?[] { p.Name, p.Sku, p.Barcode, p.StockQuantity, p.WholesalePrice, p.Price, p.StockQuantity * p.WholesalePrice, p.StockQuantity * p.Price }));
        }
    }
}
