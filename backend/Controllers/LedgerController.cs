using Ecommerce.Api.Domain;
using Ecommerce.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Api.Controllers
{
    public record LedgerPaymentDto(string Phone, decimal Amount, string Method, string? Note);
    public record LedgerAdjustDto(string Phone, decimal Amount, string Note);

    /// Shared rules for the customer account ("baki"): one balance per shop + customer phone number.
    public static class CreditLedger
    {
        public static async Task<decimal> BalanceAsync(ApplicationDbContext db, int companyId, string phone) =>
            await db.CreditLedger.AsNoTracking().Where(e => e.CompanyId == companyId && e.Phone == phone).SumAsync(e => (decimal?)e.Amount) ?? 0m;

        /// Serialises everything that changes one customer's balance (credit-limit checks, payments) so two
        /// simultaneous requests can't both pass a limit check. Must be called inside a transaction.
        public static Task LockAsync(ApplicationDbContext db, int companyId, string phone) =>
            db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtext({0}))", $"credit:{companyId}:{phone}");

        public static async Task<decimal> CreditLimitAsync(ApplicationDbContext db, int companyId)
        {
            var raw = await db.CompanySettings.AsNoTracking()
                .Where(s => s.CompanyId == companyId && s.Key == "credit_limit").Select(s => s.Value).FirstOrDefaultAsync();
            return decimal.TryParse(raw, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0 ? v : 0m;
        }

        /// When a credit order is cancelled, whatever the customer still owes for it is written off the account.
        /// (Money they already paid is a cash refund the shop makes by hand: the ledger only tracks what is OWED.)
        public static async Task<decimal> ReverseOrderAsync(ApplicationDbContext db, int companyId, int orderId, string phone, string? name, int? by, string note)
        {
            var outstanding = await db.CreditLedger.AsNoTracking().Where(e => e.CompanyId == companyId && e.OrderId == orderId).SumAsync(e => (decimal?)e.Amount) ?? 0m;
            if (outstanding != 0)
            {
                db.CreditLedger.Add(Entry(companyId, phone, name, "RETURN", -outstanding, orderId, null, note, by));
                await db.SaveChangesAsync();
            }
            return outstanding;
        }

        public static CreditLedgerEntry Entry(int companyId, string phone, string? name, string type, decimal amount,
            int? orderId, string? method, string? note, int? by) => new()
        {
            CompanyId = companyId, Phone = phone, CustomerName = name, EntryType = type, Amount = amount,
            OrderId = orderId, Method = method, Note = note, RecordedBy = by
        };
    }

    // The customer account: who owes the shop how much, and the full history behind each balance.
    [ApiController]
    [Route("api/ledger")]
    [Authorize]
    public class LedgerController : ControllerBase
    {
        private static readonly string[] Methods = { "cash", "bkash", "nagad", "bank", "other" };
        private readonly ApplicationDbContext _context;

        public LedgerController(ApplicationDbContext context) { _context = context; }

        // One row per customer with a non-zero or recent balance.
        [HttpGet("customers")]
        public async Task<IActionResult> Customers([FromQuery] string? search, [FromQuery] bool dueOnly = false)
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest(new { message = "Company context is required." });

            var q = _context.CreditLedger.AsNoTracking().Where(e => e.CompanyId == companyId.Value);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                q = q.Where(e => e.Phone.Contains(s) || (e.CustomerName != null && EF.Functions.ILike(e.CustomerName, "%" + s + "%")));
            }
            var rows = await q.GroupBy(e => e.Phone).Select(g => new
            {
                phone = g.Key,
                name = g.Where(e => e.CustomerName != null).OrderByDescending(e => e.Id).Select(e => e.CustomerName).FirstOrDefault(),
                balance = g.Sum(e => e.Amount),
                lastActivity = g.Max(e => e.CreatedDate)
            }).ToListAsync();

            if (dueOnly) rows = rows.Where(r => r.balance > 0).ToList();
            return Ok(rows.OrderByDescending(r => r.balance).ThenByDescending(r => r.lastActivity));
        }

        [HttpGet("summary")]
        public async Task<IActionResult> Summary()
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest(new { message = "Company context is required." });
            var balances = await _context.CreditLedger.AsNoTracking().Where(e => e.CompanyId == companyId.Value)
                .GroupBy(e => e.Phone).Select(g => g.Sum(e => e.Amount)).ToListAsync();
            return Ok(new
            {
                totalDue = balances.Where(b => b > 0).Sum(),
                customersWithDue = balances.Count(b => b > 0),
                advanceHeld = -balances.Where(b => b < 0).Sum(),
                creditLimit = await CreditLedger.CreditLimitAsync(_context, companyId.Value)
            });
        }

        [HttpGet("customers/{phone}")]
        public async Task<IActionResult> Customer(string phone)
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest(new { message = "Company context is required." });
            var p = PhoneUtil.NormalizeBd(phone);
            if (p == null) return BadRequest(new { message = "Enter a valid Bangladeshi mobile number (01XXXXXXXXX)." });

            var entries = await _context.CreditLedger.AsNoTracking().Where(e => e.CompanyId == companyId.Value && e.Phone == p)
                .OrderByDescending(e => e.Id).Take(200)
                .Select(e => new { e.Id, e.EntryType, e.Amount, e.OrderId, e.Method, e.Note, e.CreatedDate }).ToListAsync();
            var name = await _context.CreditLedger.AsNoTracking().Where(e => e.CompanyId == companyId.Value && e.Phone == p && e.CustomerName != null)
                .OrderByDescending(e => e.Id).Select(e => e.CustomerName).FirstOrDefaultAsync();
            return Ok(new
            {
                phone = p, name,
                balance = await CreditLedger.BalanceAsync(_context, companyId.Value, p),
                creditLimit = await CreditLedger.CreditLimitAsync(_context, companyId.Value),
                entries
            });
        }

        // Money received from a customer against what they owe.
        [HttpPost("payments")]
        public async Task<IActionResult> Pay([FromBody] LedgerPaymentDto dto)
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest(new { message = "Company context is required." });
            var phone = PhoneUtil.NormalizeBd(dto.Phone);
            if (phone == null) return BadRequest(new { message = "Enter a valid Bangladeshi mobile number (01XXXXXXXXX)." });
            if (dto.Amount <= 0 || dto.Amount > 10_000_000m) return BadRequest(new { message = "Enter an amount greater than zero." });
            var method = (dto.Method ?? "cash").Trim().ToLowerInvariant();
            if (!Methods.Contains(method)) return BadRequest(new { message = "Method must be cash, bkash, nagad, bank or other." });

            await using var tx = await _context.Database.BeginTransactionAsync();
            await CreditLedger.LockAsync(_context, companyId.Value, phone);
            var before = await CreditLedger.BalanceAsync(_context, companyId.Value, phone);
            if (!await _context.CreditLedger.AnyAsync(e => e.CompanyId == companyId.Value && e.Phone == phone))
                return NotFound(new { message = "This customer has no account with you yet." });

            _context.CreditLedger.Add(CreditLedger.Entry(companyId.Value, phone, null, "PAYMENT", -dto.Amount, null, method, Clip(dto.Note, 300), _context.CurrentUserId));
            await _context.SaveChangesAsync();
            await tx.CommitAsync();
            await Audit.LogAsync(_context, HttpContext, "ledger.payment", $"{phone}: received {dto.Amount} via {method}");
            return Ok(new { phone, previousBalance = before, balance = before - dto.Amount, received = dto.Amount });
        }

        // Owner-only correction (e.g. write off a small amount, fix a wrong entry). Always needs a note.
        [HttpPost("adjustments")]
        public async Task<IActionResult> Adjust([FromBody] LedgerAdjustDto dto)
        {
            if (!User.IsInRole("companyadmin") && !User.IsInRole("superadmin")) return Forbid();
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest(new { message = "Company context is required." });
            var phone = PhoneUtil.NormalizeBd(dto.Phone);
            if (phone == null) return BadRequest(new { message = "Enter a valid Bangladeshi mobile number (01XXXXXXXXX)." });
            if (dto.Amount == 0 || Math.Abs(dto.Amount) > 10_000_000m) return BadRequest(new { message = "Enter a non-zero amount." });
            if (string.IsNullOrWhiteSpace(dto.Note)) return BadRequest(new { message = "Say why you are adjusting this account." });

            await using var tx = await _context.Database.BeginTransactionAsync();
            await CreditLedger.LockAsync(_context, companyId.Value, phone);
            _context.CreditLedger.Add(CreditLedger.Entry(companyId.Value, phone, null, "ADJUST", dto.Amount, null, null, Clip(dto.Note, 300), _context.CurrentUserId));
            await _context.SaveChangesAsync();
            await tx.CommitAsync();
            await Audit.LogAsync(_context, HttpContext, "ledger.adjust", $"{phone}: {dto.Amount:+0.##;-0.##} ({dto.Note})");
            return Ok(new { phone, balance = await CreditLedger.BalanceAsync(_context, companyId.Value, phone) });
        }

        private static string? Clip(string? s, int max)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim();
            return s.Length > max ? s[..max] : s;
        }
    }
}
