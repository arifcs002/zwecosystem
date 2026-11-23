using Ecommerce.Api.Domain;
using Ecommerce.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Ecommerce.Api.Controllers
{
    public record PaymentCreateDto(int? PlanId, decimal Amount, string Method, string? Reference, int Months = 1, string? Note = null);

    // Subscription status for a shop, and the platform owner's hand-entered payment records
    // (cash / bKash / Nagad / bank). There is no online payment gateway: the owner records a
    // payment, which extends the shop's subscription by the months paid.
    [ApiController]
    [Route("api/billing")]
    [Authorize]
    public class BillingController : ControllerBase
    {
        private static readonly string[] Methods = { "cash", "bkash", "nagad", "bank", "other" };
        private readonly ApplicationDbContext _context;
        private readonly IMemoryCache _cache;

        public BillingController(ApplicationDbContext context, IMemoryCache cache)
        {
            _context = context;
            _cache = cache;
        }

        // The shop's own view: plan, what it includes, when it ends, payment history.
        [HttpGet("my")]
        public async Task<IActionResult> My()
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest(new { message = "Company context is required." });
            return Ok(await Summary(companyId.Value));
        }

        [HttpGet("companies/{companyId}")]
        public async Task<IActionResult> ForCompany(int companyId)
        {
            if (!User.IsInRole("superadmin")) return Forbid();
            if (!await _context.Companies.IgnoreQueryFilters().AnyAsync(c => c.Id == companyId)) return NotFound();
            return Ok(await Summary(companyId));
        }

        [HttpPost("companies/{companyId}/payments")]
        public async Task<IActionResult> RecordPayment(int companyId, [FromBody] PaymentCreateDto dto)
        {
            if (!User.IsInRole("superadmin")) return Forbid();
            var company = await _context.Companies.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == companyId);
            if (company == null) return NotFound();

            var method = (dto.Method ?? "").Trim().ToLowerInvariant();
            if (!Methods.Contains(method)) return BadRequest(new { message = "Payment method must be cash, bkash, nagad, bank or other." });
            if (dto.Amount < 0 || dto.Amount > 10_000_000m) return BadRequest(new { message = "Enter a valid amount." });
            if (dto.Months < 1 || dto.Months > 24) return BadRequest(new { message = "Months must be between 1 and 24." });

            int? planId = company.SubscriptionPlanId;
            if (dto.PlanId.HasValue)
            {
                if (!await _context.SubscriptionPlans.AnyAsync(p => p.Id == dto.PlanId.Value))
                    return BadRequest(new { message = "Subscription plan not found." });
                planId = dto.PlanId;
                company.SubscriptionPlanId = planId;
            }

            // Paying early extends from the current end date; paying late starts from today.
            var now = DateTime.UtcNow;
            var start = company.SubscriptionExpiresAt.HasValue && company.SubscriptionExpiresAt.Value > now ? company.SubscriptionExpiresAt.Value : now;
            var end = start.AddMonths(dto.Months);
            company.SubscriptionExpiresAt = end;

            await _context.Database.ExecuteSqlRawAsync(
                @"INSERT INTO subscription_payments (company_id, plan_id, amount, method, reference, months, period_start, period_end, recorded_by, note)
                  VALUES ({0},{1},{2},{3},{4},{5},{6},{7},{8},{9})",
                companyId, planId, dto.Amount, method, Clip(dto.Reference, 80), dto.Months, start, end, _context.CurrentUserId, Clip(dto.Note, 300));
            await _context.SaveChangesAsync();
            PlanAccess.Invalidate(_cache, companyId);
            await Audit.LogAsync(_context, HttpContext, "billing.payment", $"company {companyId}: {dto.Amount} via {method}, +{dto.Months} mo, until {end:yyyy-MM-dd}");
            return Ok(await Summary(companyId));
        }

        private async Task<object> Summary(int companyId)
        {
            var plan = await PlanAccess.GetAsync(_context, _cache, companyId);
            var planRow = plan?.PlanId == null ? null
                : await _context.SubscriptionPlans.AsNoTracking().Where(p => p.Id == plan.PlanId).Select(p => new { p.Id, p.Name, p.Price, p.BillingCycle }).FirstOrDefaultAsync();
            var now = DateTime.UtcNow;
            var payments = await _context.Database.SqlQueryRaw<PaymentRow>(
                @"SELECT id AS ""Id"", amount AS ""Amount"", method AS ""Method"", reference AS ""Reference"", months AS ""Months"",
                         period_start AS ""PeriodStart"", period_end AS ""PeriodEnd"", created_date AS ""CreatedDate""
                  FROM subscription_payments WHERE company_id = {0} ORDER BY created_date DESC LIMIT 24", companyId).ToListAsync();

            return new
            {
                plan = planRow,
                expiresAt = plan?.ExpiresAt,
                status = plan?.Status(now) ?? "active",
                daysLeft = plan?.DaysLeft(now),
                graceDays = PlanState.GraceDays,
                modules = new
                {
                    pos = plan?.IsEnabled("pos_enabled") ?? true,
                    ecommerce = plan?.IsEnabled("ecommerce_enabled") ?? true,
                    multiStaff = plan?.IsEnabled("multi_staff") ?? true
                },
                limits = new { maxProducts = plan?.Limit("max_products") },
                payments
            };
        }

        private static string? Clip(string? s, int max)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim();
            return s.Length > max ? s[..max] : s;
        }

        public class PaymentRow
        {
            public int Id { get; set; }
            public decimal Amount { get; set; }
            public string Method { get; set; } = "";
            public string? Reference { get; set; }
            public int Months { get; set; }
            public DateTime PeriodStart { get; set; }
            public DateTime PeriodEnd { get; set; }
            public DateTime CreatedDate { get; set; }
        }
    }
}
