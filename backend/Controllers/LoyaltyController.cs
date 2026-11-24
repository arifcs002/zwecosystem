using System.Globalization;
using Ecommerce.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Api.Controllers
{
    public record LoyaltySettingsDto(bool Enabled, decimal EarnPer100, decimal PointValue, int MinRedeem);
    public record LoyaltyAdjustDto(string Phone, int Points, string? Note);

    // Customer loyalty points: shown at the till, configured and corrected by the owner.
    [ApiController]
    [Route("api/loyalty")]
    [Authorize]
    public class LoyaltyController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        public LoyaltyController(ApplicationDbContext context) { _context = context; }

        [HttpGet("settings")]
        public async Task<IActionResult> GetSettings()
        {
            var cid = _context.CompanyId;
            if (!cid.HasValue) return BadRequest(new { message = "Company context is required." });
            var c = await Loyalty.ConfigAsync(_context, cid.Value);
            return Ok(new { enabled = c.Enabled, earnPer100 = c.EarnPer100, pointValue = c.PointValue, minRedeem = c.MinRedeem });
        }

        [HttpPut("settings")]
        public async Task<IActionResult> SaveSettings([FromBody] LoyaltySettingsDto dto)
        {
            var cid = _context.CompanyId;
            if (!cid.HasValue) return BadRequest(new { message = "Company context is required." });
            if (dto.EarnPer100 < 0 || dto.EarnPer100 > 100) return BadRequest(new { message = "Points per ৳100 must be between 0 and 100." });
            if (dto.PointValue < 0 || dto.PointValue > 100) return BadRequest(new { message = "One point's value must be between ৳0 and ৳100." });
            if (dto.MinRedeem < 0 || dto.MinRedeem > 100000) return BadRequest(new { message = "Minimum points to use is not valid." });
            // Guard against a setting that gives back more than the customer paid (earn rate × point value per ৳100 > 100%).
            if (dto.EarnPer100 * dto.PointValue > 50) return BadRequest(new { message = "That would give back more than half of what customers spend. Lower the points or their value." });
            var inv = CultureInfo.InvariantCulture;
            var pairs = new (string, string)[] {
                ("loyalty_enabled", dto.Enabled ? "1" : "0"), ("loyalty_earn_per_100", dto.EarnPer100.ToString(inv)),
                ("loyalty_point_value", dto.PointValue.ToString(inv)), ("loyalty_min_redeem", dto.MinRedeem.ToString(inv)) };
            foreach (var (k, v) in pairs)
                await _context.Database.ExecuteSqlRawAsync("CALL sp_upsert_setting({0},{1},{2},{3})", cid.Value, k, v, "LOYALTY");
            await Audit.LogAsync(_context, HttpContext, "loyalty.settings", $"enabled={dto.Enabled}, earn/100={dto.EarnPer100}, value={dto.PointValue}, min={dto.MinRedeem}");
            return Ok(new { message = "Saved." });
        }

        [HttpGet("lookup")]
        public async Task<IActionResult> Lookup([FromQuery] string? phone)
        {
            var cid = _context.CompanyId;
            if (!cid.HasValue) return BadRequest(new { message = "Company context is required." });
            var c = await Loyalty.ConfigAsync(_context, cid.Value);
            var p = PhoneUtil.NormalizeBd(phone);
            if (!c.Enabled || p == null) return Ok(new { enabled = c.Enabled, phone = p, balance = 0, pointValue = c.PointValue, minRedeem = c.MinRedeem, worth = 0m });
            var bal = await Loyalty.BalanceAsync(_context, cid.Value, p);
            return Ok(new { enabled = true, phone = p, balance = bal, pointValue = c.PointValue, minRedeem = c.MinRedeem, worth = Math.Round(bal * c.PointValue, 2) });
        }

        [HttpGet("customers")]
        public async Task<IActionResult> Customers()
        {
            var cid = _context.CompanyId;
            if (!cid.HasValue) return BadRequest(new { message = "Company context is required." });
            var rows = await _context.Database.SqlQueryRaw<LoyaltyRow>(
                @"SELECT phone AS ""Phone"", SUM(points)::int AS ""Points"", MAX(created_date) AS ""LastActivity"" FROM loyalty_ledger
                  WHERE company_id = {0} GROUP BY phone HAVING SUM(points) <> 0 ORDER BY SUM(points) DESC LIMIT 200", cid.Value).ToListAsync();
            return Ok(rows);
        }

        [HttpGet("customers/{phone}")]
        public async Task<IActionResult> History(string phone)
        {
            var cid = _context.CompanyId;
            var p = PhoneUtil.NormalizeBd(phone);
            if (!cid.HasValue || p == null) return BadRequest(new { message = "A valid mobile number is needed." });
            var entries = await _context.Database.SqlQueryRaw<LoyaltyEntry>(
                @"SELECT id AS ""Id"", entry_type AS ""EntryType"", points AS ""Points"", order_id AS ""OrderId"", note AS ""Note"", created_date AS ""CreatedDate""
                  FROM loyalty_ledger WHERE company_id = {0} AND phone = {1} ORDER BY id DESC LIMIT 100", cid.Value, p).ToListAsync();
            return Ok(new { phone = p, balance = await Loyalty.BalanceAsync(_context, cid.Value, p), entries });
        }

        // Owner correction (a goodwill bonus, or fixing a mistake). The balance can never be pushed below zero.
        [HttpPost("adjustments")]
        public async Task<IActionResult> Adjust([FromBody] LoyaltyAdjustDto dto)
        {
            var cid = _context.CompanyId;
            if (!cid.HasValue) return BadRequest(new { message = "Company context is required." });
            var p = PhoneUtil.NormalizeBd(dto.Phone);
            if (p == null) return BadRequest(new { message = "Enter a valid mobile number (01XXXXXXXXX)." });
            if (dto.Points == 0 || Math.Abs(dto.Points) > 1_000_000) return BadRequest(new { message = "Enter a number of points (not zero)." });
            if (string.IsNullOrWhiteSpace(dto.Note)) return BadRequest(new { message = "Write a short reason." });
            await using var tx = await _context.Database.BeginTransactionAsync();
            await Loyalty.LockAsync(_context, cid.Value, p);
            var bal = await Loyalty.BalanceAsync(_context, cid.Value, p);
            if (bal + dto.Points < 0) return BadRequest(new { message = $"This customer has only {bal} points." });
            await Loyalty.AddAsync(_context, cid.Value, p, "ADJUST", dto.Points, null, dto.Note.Trim().Length > 200 ? dto.Note.Trim()[..200] : dto.Note.Trim(), _context.CurrentUserId);
            await tx.CommitAsync();
            await Audit.LogAsync(_context, HttpContext, "loyalty.adjust", $"{p}: {dto.Points:+#;-#} points");
            return Ok(new { balance = bal + dto.Points });
        }

        public sealed class LoyaltyRow { public string Phone { get; set; } = ""; public int Points { get; set; } public DateTime LastActivity { get; set; } }
        public sealed class LoyaltyEntry { public int Id { get; set; } public string EntryType { get; set; } = ""; public int Points { get; set; } public int? OrderId { get; set; } public string? Note { get; set; } public DateTime CreatedDate { get; set; } }
    }
}
