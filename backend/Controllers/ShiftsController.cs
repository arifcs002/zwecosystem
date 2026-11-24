using System.Globalization;
using Ecommerce.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Api.Controllers
{
    public record ShiftOpenDto(decimal OpeningCash);
    public record ShiftCloseDto(decimal CountedCash, string? Note);

    // A cashier's shift: count the drawer when you start, and again when you leave. The system works out what
    // should be there from this cashier's own cash sales, cash received on accounts and cash refunds.
    [ApiController]
    [Route("api/shifts")]
    [Authorize]
    public class ShiftsController : ControllerBase
    {
        private const int BdOffsetHours = 6;
        private readonly ApplicationDbContext _context;
        public ShiftsController(ApplicationDbContext context) { _context = context; }

        public sealed class ShiftRow
        {
            public int Id { get; set; } public int UserId { get; set; } public string? UserName { get; set; }
            public DateTime OpenedAt { get; set; } public decimal OpeningCash { get; set; }
            public DateTime? ClosedAt { get; set; } public decimal? ExpectedCash { get; set; } public decimal? CountedCash { get; set; } public string? Note { get; set; }
        }
        public sealed class CashierRow { public int? UserId { get; set; } public string? UserName { get; set; } public int Orders { get; set; } public decimal Revenue { get; set; } public decimal Discounts { get; set; } public decimal Cash { get; set; } }

        private async Task<decimal> Sum(string sql, params object[] args) =>
            (await _context.Database.SqlQueryRaw<decimal>(sql, args).ToListAsync()).FirstOrDefault();

        // What should be in this cashier's drawer right now.
        private async Task<object> Figures(int companyId, int userId, DateTime from, DateTime to, decimal opening)
        {
            var cashSales = await Sum(@"SELECT COALESCE(SUM(total),0) AS ""Value"" FROM orders WHERE company_id={0} AND sales_staff_id={1} AND sale_type='POS' AND status='COMPLETED' AND payment_method='CASH' AND created_date >= {2} AND created_date <= {3}", companyId, userId, from, to);
            var onAccount = await Sum(@"SELECT COALESCE(-SUM(amount),0) AS ""Value"" FROM credit_ledger WHERE company_id={0} AND recorded_by={1} AND entry_type='PAYMENT' AND lower(COALESCE(method,''))='cash' AND created_date >= {2} AND created_date <= {3}", companyId, userId, from, to);
            var refunds = await Sum(@"SELECT COALESCE(SUM(cash_refunded),0) AS ""Value"" FROM order_returns WHERE company_id={0} AND recorded_by={1} AND created_date >= {2} AND created_date <= {3}", companyId, userId, from, to);
            return new { openingCash = opening, cashSales, cashReceivedOnAccount = onAccount, cashRefunds = refunds, expectedCash = opening + cashSales + onAccount - refunds };
        }

        private static decimal Expected(object f) => (decimal)f.GetType().GetProperty("expectedCash")!.GetValue(f)!;

        [HttpGet("current")]
        public async Task<IActionResult> Current()
        {
            var cid = _context.CompanyId; var uid = _context.CurrentUserId;
            if (!cid.HasValue || !uid.HasValue) return BadRequest(new { message = "Sign in again." });
            var s = (await _context.Database.SqlQueryRaw<ShiftRow>(
                @"SELECT id AS ""Id"", user_id AS ""UserId"", NULL::text AS ""UserName"", opened_at AS ""OpenedAt"", opening_cash AS ""OpeningCash"", closed_at AS ""ClosedAt"", expected_cash AS ""ExpectedCash"", counted_cash AS ""CountedCash"", note AS ""Note""
                  FROM cashier_shifts WHERE company_id={0} AND user_id={1} AND closed_at IS NULL", cid.Value, uid.Value).ToListAsync()).FirstOrDefault();
            if (s == null) return Ok(new { open = false });
            return Ok(new { open = true, shift = s, figures = await Figures(cid.Value, uid.Value, s.OpenedAt, DateTime.UtcNow, s.OpeningCash) });
        }

        [HttpPost("open")]
        public async Task<IActionResult> Open([FromBody] ShiftOpenDto dto)
        {
            var cid = _context.CompanyId; var uid = _context.CurrentUserId;
            if (!cid.HasValue || !uid.HasValue) return BadRequest(new { message = "Sign in again." });
            if (dto.OpeningCash < 0 || dto.OpeningCash > 100_000_000) return BadRequest(new { message = "Opening cash is not valid." });
            // the unique index lets exactly one of two simultaneous opens win
            var n = await _context.Database.ExecuteSqlRawAsync(
                @"INSERT INTO cashier_shifts (company_id, user_id, opening_cash) VALUES ({0},{1},{2}) ON CONFLICT DO NOTHING", cid.Value, uid.Value, dto.OpeningCash);
            if (n == 0) return Conflict(new { message = "You already have an open shift." });
            return Ok(new { message = "Shift opened." });
        }

        [HttpPost("close")]
        public async Task<IActionResult> Close([FromBody] ShiftCloseDto dto)
        {
            var cid = _context.CompanyId; var uid = _context.CurrentUserId;
            if (!cid.HasValue || !uid.HasValue) return BadRequest(new { message = "Sign in again." });
            if (dto.CountedCash < 0 || dto.CountedCash > 100_000_000) return BadRequest(new { message = "Counted cash is not valid." });
            var s = (await _context.Database.SqlQueryRaw<ShiftRow>(
                @"SELECT id AS ""Id"", user_id AS ""UserId"", NULL::text AS ""UserName"", opened_at AS ""OpenedAt"", opening_cash AS ""OpeningCash"", closed_at AS ""ClosedAt"", expected_cash AS ""ExpectedCash"", counted_cash AS ""CountedCash"", note AS ""Note""
                  FROM cashier_shifts WHERE company_id={0} AND user_id={1} AND closed_at IS NULL", cid.Value, uid.Value).ToListAsync()).FirstOrDefault();
            if (s == null) return NotFound(new { message = "You have no open shift." });
            var now = DateTime.UtcNow;
            var expected = Expected(await Figures(cid.Value, uid.Value, s.OpenedAt, now, s.OpeningCash));
            var diff = Math.Round(dto.CountedCash - expected, 2);
            var note = string.IsNullOrWhiteSpace(dto.Note) ? null : (dto.Note.Trim().Length > 300 ? dto.Note.Trim()[..300] : dto.Note.Trim());
            if (diff != 0 && note == null) return BadRequest(new { message = "The drawer is " + (diff < 0 ? "short" : "over") + $" by ৳{Math.Abs(diff):0.##}. Write what you think caused it." });
            var n = await _context.Database.ExecuteSqlRawAsync(
                @"UPDATE cashier_shifts SET closed_at={0}, expected_cash={1}, counted_cash={2}, note={3}::text WHERE id={4} AND closed_at IS NULL", now, expected, dto.CountedCash, note, s.Id);
            if (n == 0) return Conflict(new { message = "This shift was already closed." });
            await Audit.LogAsync(_context, HttpContext, "shift.close", $"expected {expected:0.##}, counted {dto.CountedCash:0.##}");
            return Ok(new { expectedCash = expected, countedCash = dto.CountedCash, difference = diff });
        }

        // Manager view: every shift in the range with who worked it and any difference.
        [HttpGet("history")]
        public async Task<IActionResult> History([FromQuery] string? from, [FromQuery] string? to)
        {
            var cid = _context.CompanyId;
            if (!cid.HasValue) return BadRequest(new { message = "Company context is required." });
            if (!Range(from, to, out var s, out var e, out var err)) return BadRequest(new { message = err });
            var rows = await _context.Database.SqlQueryRaw<ShiftRow>(
                @"SELECT sh.id AS ""Id"", sh.user_id AS ""UserId"", TRIM(COALESCE(u.first_name,'') || ' ' || COALESCE(u.last_name,'')) AS ""UserName"", sh.opened_at AS ""OpenedAt"", sh.opening_cash AS ""OpeningCash"",
                         sh.closed_at AS ""ClosedAt"", sh.expected_cash AS ""ExpectedCash"", sh.counted_cash AS ""CountedCash"", sh.note AS ""Note""
                  FROM cashier_shifts sh LEFT JOIN users u ON u.id = sh.user_id
                  WHERE sh.company_id={0} AND sh.opened_at >= {1} AND sh.opened_at < {2} ORDER BY sh.opened_at DESC LIMIT 500", cid.Value, s, e).ToListAsync();
            return Ok(rows);
        }

        // Manager view: sales per cashier in the range (POS sales that were completed).
        [HttpGet("cashiers")]
        public async Task<IActionResult> Cashiers([FromQuery] string? from, [FromQuery] string? to)
        {
            var cid = _context.CompanyId;
            if (!cid.HasValue) return BadRequest(new { message = "Company context is required." });
            if (!Range(from, to, out var s, out var e, out var err)) return BadRequest(new { message = err });
            var rows = await _context.Database.SqlQueryRaw<CashierRow>(
                @"SELECT o.sales_staff_id AS ""UserId"", TRIM(COALESCE(u.first_name,'') || ' ' || COALESCE(u.last_name,'')) AS ""UserName"", COUNT(*)::int AS ""Orders"",
                         COALESCE(SUM(o.total),0) AS ""Revenue"", COALESCE(SUM(o.discount),0) AS ""Discounts"",
                         COALESCE(SUM(CASE WHEN o.payment_method='CASH' THEN o.total ELSE 0 END),0) AS ""Cash""
                  FROM orders o LEFT JOIN users u ON u.id = o.sales_staff_id
                  WHERE o.company_id={0} AND o.sale_type='POS' AND o.status='COMPLETED' AND o.created_date >= {1} AND o.created_date < {2}
                  GROUP BY o.sales_staff_id, u.first_name, u.last_name ORDER BY SUM(o.total) DESC", cid.Value, s, e).ToListAsync();
            return Ok(rows);
        }

        private static bool Range(string? from, string? to, out DateTime startUtc, out DateTime endUtc, out string? error)
        {
            startUtc = endUtc = default; error = null;
            if (!DateTime.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var f) ||
                !DateTime.TryParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) { error = "from and to must be dates like 2026-11-01."; return false; }
            if (t < f) { error = "'to' must not be before 'from'."; return false; }
            if ((t - f).TotalDays > 366) { error = "Choose a range of at most one year."; return false; }
            startUtc = DateTime.SpecifyKind(f.Date.AddHours(-BdOffsetHours), DateTimeKind.Utc);
            endUtc = DateTime.SpecifyKind(t.Date.AddDays(1).AddHours(-BdOffsetHours), DateTimeKind.Utc);
            return true;
        }
    }
}
