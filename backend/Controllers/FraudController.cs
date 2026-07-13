using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ecommerce.Api.Infrastructure;
using Ecommerce.Api.Models;
using Microsoft.AspNetCore.Authorization;

namespace Ecommerce.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class FraudController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IFileTextLogger _fileLogger;

        public FraudController(ApplicationDbContext context, IFileTextLogger fileLogger)
        {
            _context = context;
            _fileLogger = fileLogger;
        }

        // Orders flagged for admin review (REVIEW or BLOCK decision). PASS
        // decisions aren't surfaced here — they never needed a human look.
        [HttpGet("flagged")]
        public async Task<IActionResult> GetFlagged()
        {
            var companyId = _context.CompanyId;

            var flagged = await _context.FraudChecks
                .Where(f => (!companyId.HasValue || f.CompanyId == companyId.Value)
                         && (f.Decision == "REVIEW" || f.Decision == "BLOCK"))
                .OrderByDescending(f => f.CheckedDate)
                .Select(f => new
                {
                    f.Id,
                    f.OrderId,
                    OrderNumber = f.Order != null ? f.Order.OrderNumber : null,
                    CustomerName = f.Order != null ? f.Order.CustomerName : null,
                    CustomerPhone = f.Order != null ? f.Order.CustomerPhone : null,
                    OrderTotal = f.Order != null ? f.Order.Total : 0,
                    f.IpAddress,
                    f.RiskScore,
                    f.Flags,
                    f.Decision,
                    f.CheckedDate
                })
                .ToListAsync();

            return Ok(flagged);
        }

        // Admin override — resolves a flagged order to PASS/REVIEW/BLOCK after
        // manual review. Doesn't itself change order/payment status.
        [HttpPut("{id}/resolve")]
        public async Task<IActionResult> Resolve(int id, [FromBody] FraudResolveDto dto)
        {
            var allowed = new[] { "PASS", "REVIEW", "BLOCK" };
            var decision = dto.Decision?.ToUpperInvariant();
            if (decision == null || !allowed.Contains(decision))
                return BadRequest("Decision must be PASS, REVIEW, or BLOCK.");

            var fraudCheck = await _context.FraudChecks.FirstOrDefaultAsync(f => f.Id == id);
            if (fraudCheck == null) return NotFound("Fraud check not found");

            fraudCheck.Decision = decision;
            await _context.SaveChangesAsync();

            return Ok(new { message = "Fraud decision updated", decision });
        }
    }
}
