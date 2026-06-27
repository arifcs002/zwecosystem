using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Ecommerce.Api.Infrastructure;
using Ecommerce.Api.Models;
using Microsoft.AspNetCore.Authorization;

namespace Ecommerce.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class POSController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IFileTextLogger _fileLogger;

        public POSController(ApplicationDbContext context, IFileTextLogger fileLogger)
        {
            _context = context;
            _fileLogger = fileLogger;
        }

        [HttpGet("lookup")]
        public async Task<IActionResult> LookupProduct([FromQuery] string barcode)
        {
            var companyId = _context.CompanyId;
            var result = await _context.Database
                .SqlQueryRaw<dynamic>("SELECT * FROM sp_lookup_product({0},{1})", barcode, companyId)
                .ToListAsync();

            if (!result.Any()) return NotFound(new { message = "Product not found for the scanned barcode" });
            return Ok(result.First());
        }

        [HttpPost("checkout")]
        public async Task<IActionResult> POSCheckout([FromBody] POSCheckoutDto dto)
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest("Company context is required.");

            int cashierId = 0;
            int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out cashierId);

            decimal subtotal = 0;
            var productIds = new List<int>();
            var quantities  = new List<int>();
            var prices      = new List<decimal>();

            foreach (var item in dto.Items)
            {
                var product = await _context.Products.FindAsync(item.ProductId);
                if (product == null) return BadRequest(new { message = $"Product ID {item.ProductId} not found." });
                if (product.StockQuantity < item.Quantity)
                    return BadRequest(new { message = $"Insufficient stock for '{product.Name}'. Available: {product.StockQuantity}" });

                subtotal += product.Price * item.Quantity;
                productIds.Add(item.ProductId);
                quantities.Add(item.Quantity);
                prices.Add(product.Price);
            }

            var total = Math.Max(subtotal - dto.Discount, 0);
            var orderNumber   = $"POS-{DateTime.UtcNow.Ticks}";
            var paymentStatus = dto.PaymentMethod == "CASH" ? "PAID" : "PENDING";

            var orderId = (await _context.Database.SqlQueryRaw<int>(
                "SELECT sp_checkout_order({0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15})",
                companyId.Value, orderNumber, "POS", cashierId,
                dto.CustomerName ?? "Walk-in Customer", dto.CustomerPhone ?? "",
                "COMPLETED", subtotal, dto.Discount, total,
                dto.PaymentMethod, paymentStatus,
                productIds.ToArray(), quantities.ToArray(), prices.ToArray(), cashierId
            ).ToListAsync()).FirstOrDefault();

            if (dto.PaymentMethod != "CASH" && !string.IsNullOrEmpty(dto.TransactionId))
            {
                await _context.Database.SqlQueryRaw<int>(
                    "SELECT sp_verify_payment({0},{1},{2},{3},{4},{5},{6},{7},{8})",
                    companyId.Value, orderId, dto.TransactionId, dto.PaymentMethod,
                    total, "SUCCESS", "AUTOMATED", "", ""
                ).ToListAsync();
            }

            var settings = await _context.CompanySettings
                .Where(s => s.CompanyId == companyId.Value && s.GroupName == "POS")
                .ToDictionaryAsync(s => s.Key, s => s.Value);

            return Ok(new
            {
                message = "POS Transaction completed successfully",
                orderId,
                orderNumber,
                receipt = new
                {
                    header      = settings.GetValueOrDefault("receipt_header", "Thank you!"),
                    footer      = settings.GetValueOrDefault("receipt_footer", "Please visit again."),
                    orderNumber,
                    subtotal,
                    discount    = dto.Discount,
                    total,
                    paymentMethod = dto.PaymentMethod,
                    cashierName = User.FindFirst("name")?.Value ?? "Cashier"
                }
            });
        }
    }
}
