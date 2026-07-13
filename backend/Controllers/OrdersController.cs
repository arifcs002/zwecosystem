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
    public class OrdersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IFileTextLogger _fileLogger;
        private readonly ISmsSender _smsSender;
        private readonly IFraudAnalyzer _fraudAnalyzer;

        public OrdersController(ApplicationDbContext context, IFileTextLogger fileLogger, ISmsSender smsSender, IFraudAnalyzer fraudAnalyzer)
        {
            _context = context;
            _fileLogger = fileLogger;
            _smsSender = smsSender;
            _fraudAnalyzer = fraudAnalyzer;
        }

        // Storefront guest checkout. Anonymous — the tenant is resolved from the
        // X-Tenant-ID header the public shop sends. Prices are re-read from the DB
        // (never trusted from the client) and stock is validated before the order
        // is written. Delivery fee comes from the company's own settings.
        [HttpPost("public")]
        [AllowAnonymous]
        public async Task<IActionResult> CreatePublicOrder([FromBody] PublicCheckoutDto dto)
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest(new { message = "Store context is required." });
            if (dto.Items == null || dto.Items.Count == 0) return BadRequest(new { message = "Your cart is empty." });
            if (string.IsNullOrWhiteSpace(dto.CustomerName) || string.IsNullOrWhiteSpace(dto.CustomerPhone)
                || string.IsNullOrWhiteSpace(dto.ShippingAddress))
                return BadRequest(new { message = "Name, phone and address are required." });

            decimal subtotal = 0;
            var productIds = new List<int>();
            var quantities = new List<int>();
            var prices     = new List<decimal>();

            foreach (var item in dto.Items)
            {
                if (item.Quantity <= 0) continue;
                var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == item.ProductId && p.IsDeleted == 0);
                if (product == null) return BadRequest(new { message = $"A product in your cart is no longer available." });
                if (product.StockQuantity < item.Quantity)
                    return BadRequest(new { message = $"Insufficient stock for '{product.Name}'. Available: {product.StockQuantity}" });

                subtotal += product.Price * item.Quantity;
                productIds.Add(item.ProductId);
                quantities.Add(item.Quantity);
                prices.Add(product.Price);
            }

            if (productIds.Count == 0) return BadRequest(new { message = "Your cart is empty." });

            // Delivery fee from this company's settings (free above threshold, if set).
            var settings = await _context.CompanySettings
                .Where(s => s.CompanyId == companyId.Value)
                .ToDictionaryAsync(s => s.Key, s => s.Value);
            decimal.TryParse(settings.GetValueOrDefault("delivery_charge", "0"), out var deliveryCharge);
            decimal shippingFee = deliveryCharge;
            if (decimal.TryParse(settings.GetValueOrDefault("free_delivery_above", ""), out var freeAbove)
                && freeAbove > 0 && subtotal >= freeAbove)
                shippingFee = 0;

            var total = subtotal + shippingFee;
            var orderNumber = $"WEB-{DateTime.UtcNow.Ticks}";
            var payment = string.IsNullOrWhiteSpace(dto.PaymentMethod) ? "COD" : dto.PaymentMethod;

            var orderId = (await _context.Database.SqlQueryRaw<int>(
                "SELECT sp_create_online_order({0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14})",
                companyId.Value, orderNumber,
                dto.CustomerName, dto.CustomerPhone,
                dto.ShippingAddress, dto.ShippingDistrict ?? "", dto.ShippingThana ?? "",
                dto.OrderNotes ?? "",
                subtotal, shippingFee, total, payment,
                productIds.ToArray(), quantities.ToArray(), prices.ToArray()
            ).ToListAsync()).FirstOrDefault();

            await RunFraudCheckAsync(orderId, companyId.Value, dto.CustomerPhone, total);

            return Ok(new { orderId, orderNumber, subtotal, shippingFee, total });
        }

        // Screens the just-created order for fraud signals (velocity, unusual
        // amount, etc). Advisory only — never blocks checkout; a BLOCK/REVIEW
        // decision just surfaces the order via api/fraud/flagged for admin review.
        private async Task RunFraudCheckAsync(int orderId, int companyId, string? customerPhone, decimal total)
        {
            try
            {
                var ip = Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',').FirstOrDefault()?.Trim()
                         ?? HttpContext.Connection.RemoteIpAddress?.ToString();

                var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == orderId);
                if (order == null) return;

                order.CustomerIp = ip;
                await _context.SaveChangesAsync();

                var analysis = await _fraudAnalyzer.AnalyzeAsync(order, ip);

                _context.FraudChecks.Add(new FraudCheck
                {
                    CompanyId = companyId,
                    OrderId = orderId,
                    IpAddress = ip,
                    RiskScore = analysis.RiskScore,
                    Flags = string.Join(",", analysis.Flags),
                    Decision = analysis.Decision,
                    CheckedDate = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _fileLogger.LogError("FRAUD", $"Fraud check failed for order {orderId}", ex);
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetOrders()
        {
            var companyId = _context.CompanyId ?? 0;
            var orders = await _context.Database
                .SqlQueryRaw<OrderListVm>("SELECT * FROM sp_get_orders({0})", companyId)
                .ToListAsync();
            return Ok(orders);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetOrder(int id)
        {
            var order = await _context.Database
                .SqlQueryRaw<OrderListVm>("SELECT * FROM sp_get_order({0})", id)
                .ToListAsync();
            if (!order.Any()) return NotFound();
            return Ok(order.First());
        }

        [HttpPut("{id}/status")]
        public async Task<IActionResult> UpdateOrderStatus(int id, [FromBody] StatusUpdateDto dto)
        {
            var orderEntity = await _context.Orders.FirstOrDefaultAsync(o => o.Id == id);
            if (orderEntity == null) return NotFound();

            await _context.Database.ExecuteSqlRawAsync(
                "CALL sp_update_order_status({0},{1},{2})", id, dto.Status, dto.Notes ?? "");

            // Record the change on the timeline.
            _context.OrderStatusHistory.Add(new OrderStatusHistory
            {
                OrderId     = id,
                CompanyId   = orderEntity.CompanyId,
                Status      = dto.Status,
                Note        = dto.Notes,
                ChangedBy   = _context.CurrentUserId,
                CreatedDate = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            // Best-effort SMS to the customer (only if this company configured a
            // gateway URL in Settings). Never let an SMS failure fail the request.
            await TryNotifyCustomerAsync(orderEntity, dto.Status);

            var order = await _context.Database
                .SqlQueryRaw<OrderListVm>("SELECT * FROM sp_get_order({0})", id)
                .ToListAsync();
            return Ok(order.FirstOrDefault());
        }

        // ── GET /{id}/history — the order's status timeline ────────────────────
        [HttpGet("{id}/history")]
        public async Task<IActionResult> GetOrderHistory(int id)
        {
            if (!await _context.Orders.AnyAsync(o => o.Id == id)) return NotFound();
            var history = await _context.OrderStatusHistory
                .Where(h => h.OrderId == id)
                .OrderBy(h => h.CreatedDate)
                .Select(h => new { h.Status, h.Note, h.CreatedDate, h.ChangedBy })
                .ToListAsync();
            return Ok(history);
        }

        // ── PUT /{id}/courier — set courier name + tracking number ─────────────
        [HttpPut("{id}/courier")]
        public async Task<IActionResult> UpdateCourier(int id, [FromBody] CourierUpdateDto dto)
        {
            var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == id);
            if (order == null) return NotFound();

            order.CourierName    = string.IsNullOrWhiteSpace(dto.CourierName) ? null : dto.CourierName.Trim();
            order.TrackingNumber = string.IsNullOrWhiteSpace(dto.TrackingNumber) ? null : dto.TrackingNumber.Trim();
            await _context.SaveChangesAsync();

            return Ok(new { order.Id, order.CourierName, order.TrackingNumber });
        }

        // Sends a status-update SMS if the company set an sms_api_url template in
        // its settings. Silently skips otherwise. Message text is templated per
        // status; the storefront name is included when available.
        private async Task TryNotifyCustomerAsync(Order order, string status)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(order.CustomerPhone)) return;

                var settings = await _context.CompanySettings
                    .Where(s => s.CompanyId == order.CompanyId)
                    .ToDictionaryAsync(s => s.Key, s => s.Value);

                var apiUrl = settings.GetValueOrDefault("sms_api_url");
                if (string.IsNullOrWhiteSpace(apiUrl)) return;

                var shopName = settings.GetValueOrDefault("shop_name")
                    ?? settings.GetValueOrDefault("store_name") ?? "our store";

                var msg = status.ToUpperInvariant() switch
                {
                    "PROCESSING" => $"Your order {order.OrderNumber} at {shopName} is being processed.",
                    "PACKED"     => $"Your order {order.OrderNumber} at {shopName} has been packed.",
                    "SHIPPED"    => $"Good news! Your order {order.OrderNumber} from {shopName} has been shipped"
                                    + (string.IsNullOrWhiteSpace(order.TrackingNumber) ? "." : $" (tracking: {order.TrackingNumber}).") ,
                    "COMPLETED"  => $"Your order {order.OrderNumber} from {shopName} has been delivered. Thank you!",
                    "CANCELLED"  => $"Your order {order.OrderNumber} at {shopName} has been cancelled.",
                    _             => $"Your order {order.OrderNumber} status: {status}."
                };

                await _smsSender.SendAsync(apiUrl, order.CustomerPhone, msg);
            }
            catch (Exception ex)
            {
                _fileLogger.LogError("SMS", $"Notify failed for order {order.Id}", ex);
            }
        }

        [HttpPost("{id}/cancel")]
        public async Task<IActionResult> CancelOrder(int id)
        {
            if (!await _context.Orders.AnyAsync(o => o.Id == id)) return NotFound();
            await _context.Database.ExecuteSqlRawAsync("CALL sp_cancel_order({0})", id);
            return Ok(new { message = "Order cancelled." });
        }
    }
}
