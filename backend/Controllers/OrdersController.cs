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
    public class OrdersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IFileTextLogger _fileLogger;

        public OrdersController(ApplicationDbContext context, IFileTextLogger fileLogger)
        {
            _context = context;
            _fileLogger = fileLogger;
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

            return Ok(new { orderId, orderNumber, subtotal, shippingFee, total });
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
            if (!await _context.Orders.AnyAsync(o => o.Id == id)) return NotFound();

            await _context.Database.ExecuteSqlRawAsync(
                "CALL sp_update_order_status({0},{1},{2})", id, dto.Status, dto.Notes ?? "");

            var order = await _context.Database
                .SqlQueryRaw<OrderListVm>("SELECT * FROM sp_get_order({0})", id)
                .ToListAsync();
            return Ok(order.FirstOrDefault());
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
