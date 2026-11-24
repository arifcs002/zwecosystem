using Ecommerce.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Api.Controllers
{
    // Things that need attention today, shown at the top of the dashboard.
    [ApiController]
    [Route("api/alerts")]
    [Authorize]
    public class AlertsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ISmsSender _sms;
        public AlertsController(ApplicationDbContext context, ISmsSender sms) { _context = context; _sms = sms; }

        public sealed class Extra { public int OldDueCustomers { get; set; } public decimal OldDueAmount { get; set; } public int LongShifts { get; set; } public int YesterdayNotClosed { get; set; } }

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var cid = _context.CompanyId;
            if (!cid.HasValue) return BadRequest(new { message = "Company context is required." });
            var today = DailySummary.BdToday();
            var c = await DailySummary.CountsAsync(_context, cid.Value, today);
            var cutoff = DateTime.UtcNow.AddDays(-30);
            var x = (await _context.Database.SqlQueryRaw<Extra>(
                @"SELECT
                    (SELECT COUNT(*) FROM (SELECT phone FROM credit_ledger WHERE company_id={0} GROUP BY phone HAVING SUM(amount) > 0 AND MAX(created_date) < {1}) a)::int AS ""OldDueCustomers"",
                    COALESCE((SELECT SUM(b) FROM (SELECT SUM(amount) AS b FROM credit_ledger WHERE company_id={0} GROUP BY phone HAVING SUM(amount) > 0 AND MAX(created_date) < {1}) a),0) AS ""OldDueAmount"",
                    (SELECT COUNT(*) FROM cashier_shifts WHERE company_id={0} AND closed_at IS NULL AND opened_at < NOW() - INTERVAL '14 hours')::int AS ""LongShifts"",
                    (CASE WHEN EXISTS (SELECT 1 FROM orders WHERE company_id={0} AND sale_type='POS' AND status='COMPLETED' AND created_date >= {2} AND created_date < {3})
                               AND NOT EXISTS (SELECT 1 FROM cash_closings WHERE company_id={0} AND business_date = {4}::date) THEN 1 ELSE 0 END)::int AS ""YesterdayNotClosed""",
                cid.Value, cutoff, DateTime.SpecifyKind(today.AddDays(-1).AddHours(-6), DateTimeKind.Utc), DateTime.SpecifyKind(today.AddHours(-6), DateTimeKind.Utc), today.AddDays(-1).D()).ToListAsync()).First();

            var alerts = new List<object>();
            void Add(string level, string text, string link) => alerts.Add(new { level, text, link });
            if (c.OutOfStock > 0) Add("warn", $"{c.OutOfStock} products are out of stock", "inventory");
            if (c.LowStock > 0) Add("info", $"{c.LowStock} products are running low (10 or fewer)", "reorder");
            if (c.ExpiredLots > 0) Add("danger", $"{c.ExpiredLots} lots have expired and must be written off", "expiry");
            if (c.ExpiringLots > 0) Add("warn", $"{c.ExpiringLots} lots expire in the next 14 days", "expiry");
            if (x.OldDueCustomers > 0) Add("warn", $"{x.OldDueCustomers} customers have owed money for over 30 days (৳{Math.Round(x.OldDueAmount):0})", "ledger");
            if (x.YesterdayNotClosed > 0) Add("info", "Yesterday's cash was not closed", "cash-closing");
            if (x.LongShifts > 0) Add("info", $"{x.LongShifts} cashier shifts have been open for over 14 hours", "shifts");
            return Ok(new { count = alerts.Count, alerts });
        }

        // Owner: send today's summary to the saved phone right now, to see what the evening message looks like.
        [HttpPost("summary/test")]
        public async Task<IActionResult> SendTest()
        {
            var cid = _context.CompanyId;
            if (!cid.HasValue) return BadRequest(new { message = "Company context is required." });
            var s = await _context.CompanySettings.AsNoTracking().Where(x => x.CompanyId == cid.Value && (x.Key == "daily_summary_phone" || x.Key == "sms_api_url")).ToDictionaryAsync(x => x.Key, x => x.Value);
            var phone = PhoneUtil.NormalizeBd(s.GetValueOrDefault("daily_summary_phone"));
            if (phone == null) return BadRequest(new { message = "Save the phone number for the summary first." });
            if (string.IsNullOrWhiteSpace(s.GetValueOrDefault("sms_api_url"))) return StatusCode(503, new { message = "Set up your SMS gateway first (Messages & Courier settings)." });
            var today = DailySummary.BdToday();
            var text = DailySummary.Message(await DailySummary.CountsAsync(_context, cid.Value, today), today);
            var ok = await _sms.SendAsync(s["sms_api_url"], phone, text);
            return Ok(new { sent = ok, message = text });
        }
    }
}
