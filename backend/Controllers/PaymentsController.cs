using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ecommerce.Api.Infrastructure;
using Ecommerce.Api.Infrastructure.PaymentGateway;
using Ecommerce.Api.Models;
using Microsoft.AspNetCore.Authorization;

namespace Ecommerce.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class PaymentsController : ControllerBase
    {
        private static readonly string[] AllowedProviders = { "BKASH", "NAGAD", "BANGLAQR" };

        private readonly ApplicationDbContext _context;
        private readonly IFileTextLogger _fileLogger;
        private readonly IPaymentGatewayService _gateway;

        public PaymentsController(ApplicationDbContext context, IFileTextLogger fileLogger, IPaymentGatewayService gateway)
        {
            _context = context;
            _fileLogger = fileLogger;
            _gateway = gateway;
        }

        // Lists SUCCESS payments so admin can pick one to refund. Scoped by
        // CompanyId via the entity's own query filter (no manual filtering needed).
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] string? status)
        {
            var query = _context.Payments.AsQueryable();
            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(p => p.Status == status.ToUpperInvariant());

            var payments = await query
                .OrderByDescending(p => p.CreatedDate)
                .Select(p => new
                {
                    p.Id,
                    p.OrderId,
                    OrderNumber = p.Order != null ? p.Order.OrderNumber : null,
                    p.Provider,
                    p.Amount,
                    p.Status,
                    p.TransactionId,
                    p.CreatedDate
                })
                .ToListAsync();

            return Ok(payments);
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

        // Kicks off an automated gateway payment (bKash/Nagad/BanglaQR). Writes a
        // PENDING Payment row up front so the order has a record even if the
        // customer never completes checkout on the provider's side.
        [HttpPost("initiate")]
        public async Task<IActionResult> Initiate([FromBody] PaymentInitiateDto dto)
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest("Company context is required.");

            var provider = dto.Provider.ToUpperInvariant();
            if (!AllowedProviders.Contains(provider))
                return BadRequest($"Unsupported payment provider '{dto.Provider}'.");

            if (!await _context.Orders.AnyAsync(o => o.Id == dto.OrderId)) return NotFound("Order not found");

            var result = await _gateway.InitiateAsync(provider, dto.OrderId, dto.Amount);
            if (!result.Success) return BadRequest(result.Error ?? "Failed to initiate payment.");

            var payment = new Domain.Payment
            {
                CompanyId = companyId.Value,
                OrderId = dto.OrderId,
                Provider = provider,
                Amount = dto.Amount,
                Status = "PENDING",
                PaymentType = "AUTOMATED",
                ReferenceLog = result.ProviderReference
            };
            _context.Payments.Add(payment);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Payment initiated", redirectUrl = result.RedirectUrl, paymentId = payment.Id });
        }

        // Server-to-server webhook the provider calls to confirm/deny a payment.
        // Anonymous because the provider (not a logged-in user) calls this.
        [HttpPost("callback/{provider}")]
        [AllowAnonymous]
        public async Task<IActionResult> Callback(string provider, [FromBody] object rawPayload)
        {
            var normalizedProvider = provider.ToUpperInvariant();
            if (!AllowedProviders.Contains(normalizedProvider))
                return BadRequest($"Unsupported payment provider '{provider}'.");

            var result = await _gateway.HandleCallbackAsync(normalizedProvider, rawPayload?.ToString() ?? string.Empty);
            if (!result.Success)
            {
                _fileLogger.LogError("PAYMENT_CALLBACK", $"{normalizedProvider} callback failed: {result.Error}");
                return BadRequest(result.Error ?? "Callback verification failed.");
            }

            var companyId = (await _context.Orders
                .Where(o => o.Id == result.OrderId)
                .Select(o => (int?)o.CompanyId)
                .FirstOrDefaultAsync()) ?? 0;
            if (companyId == 0) return NotFound("Order not found");

            var paymentId = (await _context.Database.SqlQueryRaw<int>(
                "SELECT sp_verify_payment({0},{1},{2},{3},{4},{5},{6},{7},{8})",
                companyId, result.OrderId, result.TransactionId, normalizedProvider,
                result.Amount, result.Status, "AUTOMATED", result.SenderNumber,
                $"Gateway callback for {normalizedProvider}"
            ).ToListAsync()).FirstOrDefault();

            return Ok(new { message = "Payment callback processed", status = result.Status, paymentId });
        }
    }
}
