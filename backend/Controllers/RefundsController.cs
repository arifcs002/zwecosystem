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
    public class RefundsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IFileTextLogger _fileLogger;

        public RefundsController(ApplicationDbContext context, IFileTextLogger fileLogger)
        {
            _context = context;
            _fileLogger = fileLogger;
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] RefundCreateDto dto)
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest("Company context is required.");

            if (!await _context.Payments.AnyAsync(p => p.Id == dto.PaymentId))
                return NotFound("Payment not found");
            if (!await _context.Orders.AnyAsync(o => o.Id == dto.OrderId))
                return NotFound("Order not found");

            var refund = new Refund
            {
                CompanyId = companyId.Value,
                PaymentId = dto.PaymentId,
                OrderId = dto.OrderId,
                Amount = dto.Amount,
                Reason = dto.Reason,
                Status = "REQUESTED"
            };
            _context.Refunds.Add(refund);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Refund requested", refundId = refund.Id });
        }

        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] string? status)
        {
            var query = _context.Refunds.AsQueryable();
            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(r => r.Status == status.ToUpperInvariant());

            var refunds = await query
                .OrderByDescending(r => r.CreatedDate)
                .Select(r => new
                {
                    r.Id,
                    r.PaymentId,
                    r.OrderId,
                    OrderNumber = r.Order != null ? r.Order.OrderNumber : null,
                    r.Amount,
                    r.Reason,
                    r.Status,
                    r.ProcessedBy,
                    r.ProcessedDate,
                    r.CreatedDate
                })
                .ToListAsync();

            return Ok(refunds);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var refund = await _context.Refunds.FirstOrDefaultAsync(r => r.Id == id);
            if (refund == null) return NotFound("Refund not found");
            return Ok(refund);
        }

        [HttpPut("{id}/approve")]
        public async Task<IActionResult> Approve(int id)
        {
            var refund = await _context.Refunds.FirstOrDefaultAsync(r => r.Id == id);
            if (refund == null) return NotFound("Refund not found");
            if (refund.Status != "REQUESTED") return BadRequest($"Refund is already {refund.Status}.");

            refund.Status = "APPROVED";
            await _context.SaveChangesAsync();

            return Ok(new { message = "Refund approved", status = refund.Status });
        }

        [HttpPut("{id}/reject")]
        public async Task<IActionResult> Reject(int id, [FromBody] RefundRejectDto dto)
        {
            var refund = await _context.Refunds.FirstOrDefaultAsync(r => r.Id == id);
            if (refund == null) return NotFound("Refund not found");
            if (refund.Status != "REQUESTED") return BadRequest($"Refund is already {refund.Status}.");

            refund.Status = "REJECTED";
            refund.Reason = dto.Reason ?? refund.Reason;
            await _context.SaveChangesAsync();

            return Ok(new { message = "Refund rejected", status = refund.Status });
        }

        // Admin confirms the money has been sent back to the customer manually
        // (outside the system) — cascades Payment/Order payment_status to REFUNDED.
        [HttpPut("{id}/complete")]
        public async Task<IActionResult> Complete(int id)
        {
            var refund = await _context.Refunds.FirstOrDefaultAsync(r => r.Id == id);
            if (refund == null) return NotFound("Refund not found");
            if (refund.Status != "APPROVED") return BadRequest("Refund must be APPROVED before it can be completed.");

            var processedBy = _context.CurrentUserId ?? 0;

            await _context.Database.SqlQueryRaw<int>(
                "SELECT sp_complete_refund({0},{1})", id, processedBy
            ).ToListAsync();

            return Ok(new { message = "Refund completed", status = "COMPLETED" });
        }
    }
}
