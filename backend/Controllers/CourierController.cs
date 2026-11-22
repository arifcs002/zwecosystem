using Ecommerce.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Api.Controllers
{
    // Books an order with the shop's own courier account and keeps its parcel status in sync.
    // Credentials live in company settings under secret_* keys (never returned by GET /settings).
    [ApiController]
    [Route("api/courier")]
    [Authorize]
    public class CourierController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ISteadfastClient _steadfast;
        private readonly IFileTextLogger _log;

        public CourierController(ApplicationDbContext context, ISteadfastClient steadfast, IFileTextLogger log)
        {
            _context = context;
            _steadfast = steadfast;
            _log = log;
        }

        [HttpPost("steadfast/orders/{orderId}")]
        public async Task<IActionResult> Book(int orderId)
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest(new { message = "Company context is required." });

            var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == orderId); // tenant-filtered
            if (order == null) return NotFound();
            if (order.Status == "CANCELLED") return BadRequest(new { message = "A cancelled order can't be sent to the courier." });
            if (!string.IsNullOrEmpty(order.CourierConsignmentId))
                return Conflict(new { message = "This order is already booked with a courier.", order.CourierConsignmentId, order.TrackingNumber });
            if (string.IsNullOrWhiteSpace(order.ShippingAddress) || string.IsNullOrWhiteSpace(order.CustomerName))
                return BadRequest(new { message = "The order needs a customer name and delivery address." });

            var phone = PhoneUtil.NormalizeBd(order.CustomerPhone);
            if (phone == null) return BadRequest(new { message = "The customer's phone isn't a valid Bangladeshi mobile number." });

            var (key, secret) = await Credentials(companyId.Value);
            if (key == null || secret == null)
                return BadRequest(new { message = "Add your Steadfast API key and secret first (Messages & Courier settings)." });

            // Cash to collect: the full total for unpaid COD orders, nothing if already paid.
            var cod = order.PaymentStatus == "PAID" ? 0m : order.Total;
            var result = await _steadfast.CreateOrderAsync(key, secret, order.OrderNumber, order.CustomerName!, phone,
                string.Join(", ", new[] { order.ShippingAddress, order.ShippingThana, order.ShippingDistrict }.Where(s => !string.IsNullOrWhiteSpace(s))),
                cod, order.OrderNotes);

            if (!result.Ok)
            {
                _log.LogError("COURIER", $"Steadfast booking failed for order {order.Id}: {result.Error}", null);
                return StatusCode(502, new { message = result.Error ?? "The courier rejected the booking." });
            }

            order.CourierName = "Steadfast";
            order.CourierConsignmentId = result.ConsignmentId;
            order.TrackingNumber = result.TrackingCode ?? result.ConsignmentId;
            order.CourierStatus = result.Status;
            await _context.SaveChangesAsync();
            await Audit.LogAsync(_context, HttpContext, "courier.book", $"{order.OrderNumber} -> Steadfast {order.TrackingNumber}");
            return Ok(new { order.CourierName, order.TrackingNumber, order.CourierConsignmentId, order.CourierStatus });
        }

        [HttpGet("steadfast/orders/{orderId}/status")]
        public async Task<IActionResult> Refresh(int orderId)
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest(new { message = "Company context is required." });
            var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == orderId);
            if (order == null) return NotFound();
            if (string.IsNullOrEmpty(order.CourierConsignmentId)) return BadRequest(new { message = "This order hasn't been booked with Steadfast." });

            var (key, secret) = await Credentials(companyId.Value);
            if (key == null || secret == null) return BadRequest(new { message = "Steadfast credentials are missing." });

            var result = await _steadfast.GetStatusAsync(key, secret, order.CourierConsignmentId);
            if (!result.Ok) return StatusCode(502, new { message = result.Error });
            order.CourierStatus = result.Status;
            await _context.SaveChangesAsync();
            return Ok(new { order.CourierStatus });
        }

        private async Task<(string? Key, string? Secret)> Credentials(int companyId)
        {
            var s = await _context.CompanySettings.AsNoTracking()
                .Where(x => x.CompanyId == companyId && (x.Key == "secret_steadfast_api_key" || x.Key == "secret_steadfast_secret_key"))
                .ToDictionaryAsync(x => x.Key, x => x.Value);
            string? Get(string k) => s.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
            return (Get("secret_steadfast_api_key"), Get("secret_steadfast_secret_key"));
        }
    }
}
