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
    public class PaymentsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IFileTextLogger _fileLogger;

        public PaymentsController(ApplicationDbContext context, IFileTextLogger fileLogger)
        {
            _context = context;
            _fileLogger = fileLogger;
        }

        [HttpPost("mfs-verify")]
        public async Task<IActionResult> VerifyMfsPayment([FromBody] MfsPaymentVerifyDto dto)
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest("Company context is required.");
            if (!await _context.Orders.AnyAsync(o => o.Id == dto.OrderId)) return NotFound("Order not found");

            var referenceLog = dto.ReferenceLog ?? $"Manual MFS Verification for TrxID {dto.TransactionId}";

            var paymentId = (await _context.Database.SqlQueryRaw<int>(
                "SELECT sp_verify_payment({0},{1},{2},{3},{4},{5},{6},{7},{8})",
                companyId.Value, dto.OrderId, dto.TransactionId, dto.Provider.ToUpper(),
                dto.Amount, "SUCCESS", "MANUAL", dto.SenderNumber, referenceLog
            ).ToListAsync()).FirstOrDefault();

            return Ok(new { message = "MFS Transaction successfully logged and verified", status = "SUCCESS", paymentId });
        }
    }
}
