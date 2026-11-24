using System.Text.Json;
using Ecommerce.Api.Domain;
using Ecommerce.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Api.Controllers
{
    public record CashCloseDto(string Date, decimal CountedCash, decimal? OpeningCash, string? Note);

    // End-of-day cash check: from the day's records, how much cash SHOULD be in the drawer, versus what was counted.
    // "Cash" here means money physically handed over at the counter. Online/COD orders are not counted (the courier holds that money),
    // and every expense is assumed to be paid from the drawer.
    [ApiController]
    [Route("api/cashbook")]
    [Authorize]
    public class CashbookController : ControllerBase
    {
        private const int BdOffsetHours = 6;      // Bangladesh is UTC+6 all year
        private readonly ApplicationDbContext _context;
        public CashbookController(ApplicationDbContext context) { _context = context; }

        private static DateTime BdToday() => DateTime.UtcNow.AddHours(BdOffsetHours).Date;
        private static bool TryDay(string? s, out DateTime d) => DateTime.TryParseExact(s, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out d);

        private record Figures(decimal CashSales, decimal ExchangeCreditApplied, decimal CashReceivedOnAccount, decimal CashRefunds,
            decimal Expenses, decimal SupplierCashPayments, decimal NonCashSales, decimal SoldOnCredit, decimal NonCashReceivedOnAccount);

        private async Task<Figures> Compute(int companyId, DateTime day)
        {
            var start = DateTime.SpecifyKind(day.AddHours(-BdOffsetHours), DateTimeKind.Utc);
            var end = start.AddDays(1);

            var pos = _context.Orders.AsNoTracking().Where(o => o.SaleType == "POS" && o.Status == "COMPLETED" && o.CreatedDate >= start && o.CreatedDate < end);
            var cashSales = await pos.Where(o => o.PaymentMethod == "CASH").SumAsync(o => (decimal?)o.Total) ?? 0m;
            var nonCash = await pos.Where(o => o.PaymentMethod != "CASH" && o.PaymentMethod != "CREDIT").SumAsync(o => (decimal?)o.Total) ?? 0m;
            var onCredit = await pos.Where(o => o.PaymentMethod == "CREDIT").SumAsync(o => (decimal?)o.Total) ?? 0m;

            var returns = _context.OrderReturns.AsNoTracking().Where(r => r.CreatedDate >= start && r.CreatedDate < end);
            // An exchange's new sale is rung up at full value, but part of it was paid with the returned goods' value, not cash.
            var exchangeCredit = await returns.Where(r => r.Kind == "EXCHANGE").SumAsync(r => (decimal?)(r.RefundAmount - r.CashRefunded)) ?? 0m;
            var cashRefunds = await returns.SumAsync(r => (decimal?)r.CashRefunded) ?? 0m;

            var received = _context.CreditLedger.AsNoTracking().Where(e => e.EntryType == "PAYMENT" && e.CreatedDate >= start && e.CreatedDate < end);
            var cashOnAccount = -(await received.Where(e => e.Method == "cash").SumAsync(e => (decimal?)e.Amount) ?? 0m);
            var nonCashOnAccount = -(await received.Where(e => e.Method != "cash").SumAsync(e => (decimal?)e.Amount) ?? 0m);

            var expenses = await _context.Expenses.AsNoTracking().Where(e => e.ExpenseDate == day).SumAsync(e => (decimal?)e.Amount) ?? 0m;
            var supplierCash = -(await _context.SupplierLedger.AsNoTracking().Where(e => e.EntryType == "PAYMENT" && e.Method == "cash" && e.CreatedDate >= start && e.CreatedDate < end).SumAsync(e => (decimal?)e.Amount) ?? 0m);

            return new Figures(cashSales, exchangeCredit, cashOnAccount, cashRefunds, expenses, supplierCash, nonCash, onCredit, nonCashOnAccount);
        }

        private static decimal Expected(decimal opening, Figures f) =>
            opening + f.CashSales - f.ExchangeCreditApplied + f.CashReceivedOnAccount - f.CashRefunds - f.Expenses - f.SupplierCashPayments;

        private async Task<decimal?> PreviousCountedCash(DateTime day) =>
            await _context.CashClosings.AsNoTracking().Where(c => c.BusinessDate < day).OrderByDescending(c => c.BusinessDate).Select(c => (decimal?)c.CountedCash).FirstOrDefaultAsync();

        private object View(DateTime day, decimal opening, Figures f) => new
        {
            date = day.ToString("yyyy-MM-dd"), openingCash = opening,
            cashSales = f.CashSales, exchangeCreditApplied = f.ExchangeCreditApplied, cashReceivedOnAccount = f.CashReceivedOnAccount,
            cashRefunds = f.CashRefunds, expenses = f.Expenses, supplierCashPayments = f.SupplierCashPayments,
            expectedCash = Expected(opening, f),
            info = new { nonCashSales = f.NonCashSales, soldOnCredit = f.SoldOnCredit, nonCashReceivedOnAccount = f.NonCashReceivedOnAccount }
        };

        [HttpGet("day")]
        public async Task<IActionResult> Day([FromQuery] string? date)
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest(new { message = "Company context is required." });
            var day = BdToday();
            if (date != null && !TryDay(date, out day)) return BadRequest(new { message = "Date must look like 2026-11-24." });
            if (day > BdToday()) return BadRequest(new { message = "That day hasn't happened yet." });

            var closing = await _context.CashClosings.AsNoTracking().FirstOrDefaultAsync(c => c.BusinessDate == day);
            var f = await Compute(companyId.Value, day);
            var opening = closing?.OpeningCash ?? await PreviousCountedCash(day) ?? 0m;
            return Ok(new
            {
                figures = View(day, opening, f),
                openingKnown = closing != null || await PreviousCountedCash(day) != null,
                closing = closing == null ? null : new { closing.CountedCash, closing.ExpectedCash, closing.Difference, closing.Note, closing.ClosedAt }
            });
        }

        [HttpGet("closings")]
        public async Task<IActionResult> Closings()
        {
            var rows = await _context.CashClosings.AsNoTracking().OrderByDescending(c => c.BusinessDate).Take(60)
                .Select(c => new { date = c.BusinessDate, c.OpeningCash, c.ExpectedCash, c.CountedCash, c.Difference, c.Note, c.ClosedAt }).ToListAsync();
            return Ok(rows);
        }

        [HttpPost("close")]
        public async Task<IActionResult> Close([FromBody] CashCloseDto dto)
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest(new { message = "Company context is required." });
            if (!TryDay(dto.Date, out var day)) return BadRequest(new { message = "Date must look like 2026-11-24." });
            if (day > BdToday()) return BadRequest(new { message = "You can't close a day that hasn't happened yet." });
            if (day < BdToday().AddDays(-60)) return BadRequest(new { message = "You can only close the last 60 days." });
            if (dto.CountedCash < 0 || dto.CountedCash > 1_000_000_000m) return BadRequest(new { message = "Enter the cash you counted." });

            await using var tx = await _context.Database.BeginTransactionAsync();
            await _context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtext({0}))", $"cashclose:{companyId.Value}:{dto.Date}");
            if (await _context.CashClosings.AnyAsync(c => c.BusinessDate == day))
                return Conflict(new { message = "This day has already been closed." });

            var previous = await PreviousCountedCash(day);
            if (previous == null && dto.OpeningCash is < 0) return BadRequest(new { message = "Opening cash can't be negative." });
            var opening = previous ?? dto.OpeningCash ?? 0m;          // after the first closing, yesterday's counted cash is the opening
            var f = await Compute(companyId.Value, day);
            var expected = Math.Round(Expected(opening, f), 2);
            var counted = Math.Round(dto.CountedCash, 2);
            var diff = counted - expected;
            if (diff != 0 && string.IsNullOrWhiteSpace(dto.Note))
                return BadRequest(new { message = $"The drawer is {(diff < 0 ? "short" : "over")} by ৳{Math.Abs(diff):0.##}. Write what you think caused it." });

            var closing = new CashClosing
            {
                CompanyId = companyId.Value, BusinessDate = day, OpeningCash = opening, ExpectedCash = expected, CountedCash = counted, Difference = diff,
                Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim()[..Math.Min(300, dto.Note.Trim().Length)],
                Breakdown = JsonSerializer.Serialize(View(day, opening, f)), ClosedBy = _context.CurrentUserId
            };
            _context.CashClosings.Add(closing);
            await _context.SaveChangesAsync();
            await tx.CommitAsync();
            await Audit.LogAsync(_context, HttpContext, "cash.close", $"{dto.Date}: expected {expected}, counted {counted}, difference {diff}");
            return Ok(new { closing.Id, date = dto.Date, expectedCash = expected, countedCash = counted, difference = diff });
        }
    }
}
