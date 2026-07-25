using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ecommerce.Api.Domain;
using Ecommerce.Api.Infrastructure;
using Ecommerce.Api.Models;
using Microsoft.AspNetCore.Authorization;

namespace Ecommerce.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class SubscriptionPlansController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IFileTextLogger _fileLogger;

        public SubscriptionPlansController(ApplicationDbContext context, IFileTextLogger fileLogger)
        {
            _context = context;
            _fileLogger = fileLogger;
        }

        // Any authenticated user can view the plan catalog (needed so company
        // admins can see what other tiers offer, e.g. for an upgrade prompt).
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var plans = await _context.SubscriptionPlans
                .OrderBy(p => p.Price)
                .ToListAsync();
            return Ok(plans);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var plan = await _context.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == id);
            if (plan == null) return NotFound();
            return Ok(plan);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] SubscriptionPlan dto)
        {
            if (!User.IsInRole("superadmin")) return Forbid();
            if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest(new { message = "Plan name is required." });

            var plan = new SubscriptionPlan
            {
                Name = dto.Name,
                Price = dto.Price,
                BillingCycle = string.IsNullOrWhiteSpace(dto.BillingCycle) ? "monthly" : dto.BillingCycle,
                Features = dto.Features
            };
            _context.SubscriptionPlans.Add(plan);
            await _context.SaveChangesAsync();
            return Ok(plan);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] SubscriptionPlan dto)
        {
            if (!User.IsInRole("superadmin")) return Forbid();

            var plan = await _context.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == id);
            if (plan == null) return NotFound();
            if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest(new { message = "Plan name is required." });

            plan.Name = dto.Name;
            plan.Price = dto.Price;
            plan.BillingCycle = string.IsNullOrWhiteSpace(dto.BillingCycle) ? plan.BillingCycle : dto.BillingCycle;
            plan.Features = dto.Features;
            await _context.SaveChangesAsync();
            return Ok(plan);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            if (!User.IsInRole("superadmin")) return Forbid();

            var plan = await _context.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == id);
            if (plan == null) return NotFound();
            if (await _context.Companies.IgnoreQueryFilters().AnyAsync(c => c.SubscriptionPlanId == id))
                return BadRequest(new { message = "Cannot delete a plan that companies are currently subscribed to." });

            plan.IsDeleted = 1;
            await _context.SaveChangesAsync();
            return Ok(new { message = "Plan deleted." });
        }

        // Superadmin — assign a company to a plan.
        [HttpPut("assign/{companyId}")]
        public async Task<IActionResult> AssignToCompany(int companyId, [FromBody] AssignPlanDto dto)
        {
            if (!User.IsInRole("superadmin")) return Forbid();

            var company = await _context.Companies.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == companyId);
            if (company == null) return NotFound(new { message = "Company not found." });
            if (!await _context.SubscriptionPlans.AnyAsync(p => p.Id == dto.SubscriptionPlanId))
                return BadRequest(new { message = "Subscription plan not found." });

            company.SubscriptionPlanId = dto.SubscriptionPlanId;
            await _context.SaveChangesAsync();
            return Ok(new { message = "Plan assigned.", companyId, dto.SubscriptionPlanId });
        }

        // Company admin — current plan + live usage vs limits (parsed from Features JSON).
        [HttpGet("my-plan")]
        public async Task<IActionResult> GetMyPlan()
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest(new { message = "Company context is required." });

            var company = await _context.Companies.IgnoreQueryFilters()
                .Include(c => c.SubscriptionPlan)
                .FirstOrDefaultAsync(c => c.Id == companyId.Value);
            if (company?.SubscriptionPlan == null)
                return Ok(new { plan = (object?)null, usage = new { } });

            var productCount = await _context.Products.CountAsync(p => p.CompanyId == companyId.Value);
            var userCount = await _context.Users.CountAsync(u => u.CompanyId == companyId.Value);
            var orderCount = await _context.Orders.CountAsync(o => o.CompanyId == companyId.Value);

            return Ok(new
            {
                plan = company.SubscriptionPlan,
                usage = new { products = productCount, users = userCount, orders = orderCount }
            });
        }
    }
}
