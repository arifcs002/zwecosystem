using Ecommerce.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Api.Controllers
{
    public record CampaignPreviewDto(string Audience, int? Days, string? Message);
    public record CampaignSendDto(string Name, string Audience, int? Days, string Message, int ExpectedCount);
    public record OptOutDto(string Phone);

    // Owner-only SMS to groups of customers (it spends real SMS credit). Uses the shop's own SMS gateway setting.
    // Safety: preview first, the sender must confirm the exact number of people, phones on the opt-out list are skipped,
    // at most 200 people per campaign and one campaign every 10 minutes.
    [ApiController]
    [Route("api/sms-campaigns")]
    [Authorize]
    public class SmsCampaignsController : ControllerBase
    {
        private const int MaxRecipients = 200;
        private static readonly string[] Audiences = { "all", "due", "points", "recent", "inactive" };
        private readonly ApplicationDbContext _context;
        private readonly ISmsSender _sms;
        public SmsCampaignsController(ApplicationDbContext context, ISmsSender sms) { _context = context; _sms = sms; }

        private sealed record Person(string Phone, string Name);
        public sealed class PhoneRow { public string Phone { get; set; } = ""; }
        public sealed class CampaignRow { public int Id { get; set; } public string Name { get; set; } = ""; public string Audience { get; set; } = ""; public string Message { get; set; } = ""; public int Recipients { get; set; } public int Sent { get; set; } public int Failed { get; set; } public DateTime CreatedDate { get; set; } }

        private async Task<List<string>> Phones(string sql, params object[] args) =>
            (await _context.Database.SqlQueryRaw<PhoneRow>(sql, args).ToListAsync()).Select(r => r.Phone).ToList();

        private async Task<(List<Person> People, int OptedOut)> Audience(int companyId, string audience, int days)
        {
            var since = DateTime.UtcNow.AddDays(-days);
            List<string> raw = audience switch
            {
                "due" => await Phones(@"SELECT phone AS ""Phone"" FROM credit_ledger WHERE company_id={0} GROUP BY phone HAVING SUM(amount) > 0", companyId),
                "points" => await Phones(@"SELECT phone AS ""Phone"" FROM loyalty_ledger WHERE company_id={0} GROUP BY phone HAVING SUM(points) > 0", companyId),
                "recent" => await Phones(@"SELECT DISTINCT customer_phone AS ""Phone"" FROM orders WHERE company_id={0} AND status <> 'CANCELLED' AND customer_phone IS NOT NULL AND created_date >= {1}", companyId, since),
                "inactive" => await Phones(@"SELECT customer_phone AS ""Phone"" FROM orders WHERE company_id={0} AND status <> 'CANCELLED' AND customer_phone IS NOT NULL GROUP BY customer_phone HAVING MAX(created_date) < {1}", companyId, since),
                _ => await Phones(@"SELECT customer_phone AS ""Phone"" FROM orders WHERE company_id={0} AND customer_phone IS NOT NULL AND customer_phone <> ''
                                    UNION SELECT phone FROM credit_ledger WHERE company_id={0} UNION SELECT phone FROM loyalty_ledger WHERE company_id={0}", companyId),
            };
            var phones = raw.Select(PhoneUtil.NormalizeBd).Where(p => p != null).Select(p => p!).Distinct().ToList();
            var optOut = (await Phones(@"SELECT phone AS ""Phone"" FROM sms_optouts WHERE company_id={0}", companyId)).ToHashSet();
            var kept = phones.Where(p => !optOut.Contains(p)).ToList();
            var names = (await _context.Orders.AsNoTracking().Where(o => o.CustomerPhone != null && o.CustomerName != null && kept.Contains(o.CustomerPhone))
                .OrderByDescending(o => o.CreatedDate).Select(o => new { o.CustomerPhone, o.CustomerName }).ToListAsync())
                .GroupBy(o => PhoneUtil.NormalizeBd(o.CustomerPhone) ?? o.CustomerPhone!).ToDictionary(g => g.Key, g => g.First().CustomerName!);
            return (kept.Select(p => new Person(p, names.TryGetValue(p, out var n) && n != "Walk-in Customer" ? n.Trim() : "")).ToList(), phones.Count - kept.Count);
        }

        private static string Render(string template, Person p)
        {
            var first = p.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            return template.Replace("{name}", first.Length > 0 ? first : "Customer");
        }

        private IActionResult? Validate(string audience, string? message, int? days, out int d)
        {
            d = Math.Clamp(days ?? 30, 1, 730);
            if (!Audiences.Contains(audience)) return BadRequest(new { message = "Choose who should get the message." });
            if (string.IsNullOrWhiteSpace(message)) return BadRequest(new { message = "Write the message." });
            if (message.Trim().Length > 300) return BadRequest(new { message = "Keep the message to 300 characters." });
            return null;
        }

        [HttpPost("preview")]
        public async Task<IActionResult> Preview([FromBody] CampaignPreviewDto dto)
        {
            var cid = _context.CompanyId;
            if (!cid.HasValue) return BadRequest(new { message = "Company context is required." });
            var bad = Validate(dto.Audience, dto.Message ?? "x", dto.Days, out var days); if (bad != null) return bad;
            var (people, optedOut) = await Audience(cid.Value, dto.Audience, days);
            var gateway = await _context.CompanySettings.AsNoTracking().Where(x => x.CompanyId == cid.Value && x.Key == "sms_api_url").Select(x => x.Value).FirstOrDefaultAsync();
            return Ok(new
            {
                count = people.Count, optedOut, tooMany = people.Count > MaxRecipients, maxRecipients = MaxRecipients,
                hasGateway = !string.IsNullOrWhiteSpace(gateway),
                sample = string.IsNullOrWhiteSpace(dto.Message) ? Array.Empty<string>() : people.Take(3).Select(p => Render(dto.Message.Trim(), p)).ToArray()
            });
        }

        [HttpPost("send")]
        public async Task<IActionResult> Send([FromBody] CampaignSendDto dto)
        {
            var cid = _context.CompanyId;
            if (!cid.HasValue) return BadRequest(new { message = "Company context is required." });
            var bad = Validate(dto.Audience, dto.Message, dto.Days, out var days); if (bad != null) return bad;
            if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest(new { message = "Give the campaign a name." });
            var gateway = await _context.CompanySettings.AsNoTracking().Where(x => x.CompanyId == cid.Value && x.Key == "sms_api_url").Select(x => x.Value).FirstOrDefaultAsync();
            if (string.IsNullOrWhiteSpace(gateway)) return StatusCode(503, new { message = "Set up your SMS gateway first (Messages & Courier settings)." });

            await using var tx = await _context.Database.BeginTransactionAsync();
            await _context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtext({0}))", $"campaign:{cid.Value}");
            var recent = (await _context.Database.SqlQueryRaw<int>(@"SELECT COUNT(*)::int AS ""Value"" FROM sms_campaigns WHERE company_id={0} AND created_date > NOW() - INTERVAL '10 minutes'", cid.Value).ToListAsync()).First();
            if (recent > 0) return StatusCode(429, new { message = "A campaign was sent in the last 10 minutes. Wait a little before sending another." });

            var (people, _) = await Audience(cid.Value, dto.Audience, days);
            if (people.Count == 0) return BadRequest(new { message = "No one matches this group." });
            if (people.Count > MaxRecipients) return BadRequest(new { message = $"That is {people.Count} people; one campaign can reach at most {MaxRecipients}. Choose a smaller group." });
            if (people.Count != dto.ExpectedCount) return Conflict(new { message = $"The group changed: it is now {people.Count} people, not {dto.ExpectedCount}. Check the preview again." });

            var name = dto.Name.Trim().Length > 80 ? dto.Name.Trim()[..80] : dto.Name.Trim();
            var id = (await _context.Database.SqlQueryRaw<int>(
                @"INSERT INTO sms_campaigns (company_id, name, audience, message, recipients, created_by) VALUES ({0},{1},{2},{3},{4},{5}::int) RETURNING id AS ""Value""",
                cid.Value, name, dto.Audience, dto.Message.Trim(), people.Count, _context.CurrentUserId).ToListAsync()).First();
            await tx.CommitAsync();

            int sent = 0, failed = 0;
            foreach (var p in people)
            {
                if (await _sms.SendAsync(gateway, p.Phone, Render(dto.Message.Trim(), p))) sent++; else failed++;
            }
            await _context.Database.ExecuteSqlRawAsync("UPDATE sms_campaigns SET sent={0}, failed={1} WHERE id={2}", sent, failed, id);
            await Audit.LogAsync(_context, HttpContext, "sms.campaign", $"{name}: {dto.Audience}, {sent} sent, {failed} failed");
            return Ok(new { id, recipients = people.Count, sent, failed });
        }

        [HttpGet]
        public async Task<IActionResult> List()
        {
            var cid = _context.CompanyId;
            if (!cid.HasValue) return BadRequest(new { message = "Company context is required." });
            return Ok(await _context.Database.SqlQueryRaw<CampaignRow>(
                @"SELECT id AS ""Id"", name AS ""Name"", audience AS ""Audience"", message AS ""Message"", recipients AS ""Recipients"", sent AS ""Sent"", failed AS ""Failed"", created_date AS ""CreatedDate""
                  FROM sms_campaigns WHERE company_id={0} ORDER BY id DESC LIMIT 50", cid.Value).ToListAsync());
        }

        [HttpGet("optouts")]
        public async Task<IActionResult> OptOuts()
        {
            var cid = _context.CompanyId;
            if (!cid.HasValue) return BadRequest(new { message = "Company context is required." });
            return Ok(await Phones(@"SELECT phone AS ""Phone"" FROM sms_optouts WHERE company_id={0} ORDER BY created_date DESC LIMIT 500", cid.Value));
        }

        [HttpPost("optouts")]
        public async Task<IActionResult> AddOptOut([FromBody] OptOutDto dto)
        {
            var cid = _context.CompanyId; var p = PhoneUtil.NormalizeBd(dto.Phone);
            if (!cid.HasValue || p == null) return BadRequest(new { message = "Enter a valid mobile number (01XXXXXXXXX)." });
            await _context.Database.ExecuteSqlRawAsync("INSERT INTO sms_optouts (company_id, phone) VALUES ({0},{1}) ON CONFLICT DO NOTHING", cid.Value, p);
            return Ok(new { phone = p });
        }

        [HttpDelete("optouts/{phone}")]
        public async Task<IActionResult> RemoveOptOut(string phone)
        {
            var cid = _context.CompanyId; var p = PhoneUtil.NormalizeBd(phone);
            if (!cid.HasValue || p == null) return BadRequest(new { message = "Enter a valid mobile number." });
            await _context.Database.ExecuteSqlRawAsync("DELETE FROM sms_optouts WHERE company_id={0} AND phone={1}", cid.Value, p);
            return NoContent();
        }
    }
}
