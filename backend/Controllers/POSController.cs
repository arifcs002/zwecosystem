using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Ecommerce.Api.Infrastructure;
using Ecommerce.Api.Models;
using Microsoft.AspNetCore.Authorization;

namespace Ecommerce.Api.Controllers
{
    public class ExistingSale
    {
        public int Id { get; set; }
        public string OrderNumber { get; set; } = "";
        public decimal Subtotal { get; set; }
        public decimal Discount { get; set; }
        public decimal Total { get; set; }
        public string? PaymentMethod { get; set; }
    }

    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class POSController : ControllerBase
    {
        private async Task<object> ReceiptFor(int companyId, string orderNumber, decimal subtotal, decimal discount, decimal total, string? method)
        {
            var settings = await _context.CompanySettings.AsNoTracking()
                .Where(s => s.CompanyId == companyId && s.GroupName == "POS")
                .ToDictionaryAsync(s => s.Key, s => s.Value);
            return new
            {
                header = settings.GetValueOrDefault("receipt_header", "Thank you!"),
                footer = settings.GetValueOrDefault("receipt_footer", "Please visit again."),
                orderNumber, subtotal, discount, total, paymentMethod = method,
                cashierName = User.FindFirst("name")?.Value ?? "Cashier"
            };
        }

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
                .SqlQueryRaw<ProductLookupVm>("SELECT * FROM sp_lookup_product({0},{1})", barcode, companyId)
                .ToListAsync();

            if (!result.Any()) return NotFound(new { message = "Product not found for the scanned barcode" });
            return Ok(result.First());
        }

        // A copy of the sellable catalogue for the cashier's device, so scanning keeps working without a
        // connection. Deliberately has NO wholesale/cost price: that must not sit in a phone's storage.
        [HttpGet("catalog")]
        public async Task<IActionResult> Catalog()
        {
            if (!_context.CompanyId.HasValue) return BadRequest(new { message = "Company context is required." });
            var rows = await _context.Products.AsNoTracking()
                .OrderBy(p => p.Name)
                .Select(p => new { p.Id, p.Name, p.Barcode, p.Sku, p.Price, p.StockQuantity, p.UniqueCode })
                .ToListAsync();
            return Ok(new { generatedAt = DateTime.UtcNow, items = rows });
        }

        [HttpPost("checkout")]
        public async Task<IActionResult> POSCheckout([FromBody] POSCheckoutDto dto)
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest("Company context is required.");

            int cashierId = 0;
            int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out cashierId);

            // Bargain mode is opt-in per company; anything else (including unset)
            // is FIXED, so a missing/garbled setting fails safe to no-discretion.
            var posMode = await _context.CompanySettings
                .Where(s => s.CompanyId == companyId.Value && s.Key == "pos_mode")
                .Select(s => s.Value).FirstOrDefaultAsync();
            bool bargainAllowed = string.Equals(posMode, "BARGAIN", StringComparison.OrdinalIgnoreCase);

            decimal subtotal = 0;
            var productIds = new List<int>();
            var quantities  = new List<int>();
            var prices      = new List<decimal>();

            if (dto.Items == null || dto.Items.Count == 0)
                return BadRequest(new { message = "The cart is empty." });
            // A zero/negative quantity would add stock and create negative revenue.
            if (dto.Items.Any(i => i.Quantity <= 0))
                return BadRequest(new { message = "Quantity must be at least 1." });
            if (dto.Items.Any(i => i.Price.HasValue && i.Price.Value < 0))
                return BadRequest(new { message = "Price cannot be negative." });
            if (dto.Discount < 0)
                return BadRequest(new { message = "Discount cannot be negative." });

            // Offline sales carry a client reference. A retry of the same sale must return the first result:
            // an advisory lock (per shop + reference) serialises concurrent retries, and the unique index on
            // orders(company_id, client_ref) is the backstop.
            var clientRef = (dto.ClientRef ?? "").Trim();
            if (clientRef.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(clientRef, "^[A-Za-z0-9_-]{8,64}$"))
                return BadRequest(new { message = "Invalid sale reference." });
            await using var tx = clientRef.Length > 0 ? await _context.Database.BeginTransactionAsync() : null;
            if (clientRef.Length > 0)
            {
                await _context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtext({0}))", $"{companyId.Value}:{clientRef}");
                var existing = (await _context.Database.SqlQueryRaw<ExistingSale>(
                    @"SELECT id AS ""Id"", order_number AS ""OrderNumber"", subtotal AS ""Subtotal"", discount AS ""Discount"", total AS ""Total"", payment_method AS ""PaymentMethod""
                      FROM orders WHERE company_id = {0} AND client_ref = {1} LIMIT 1", companyId.Value, clientRef).ToListAsync()).FirstOrDefault();
                if (existing != null)
                    return Ok(new { message = "This sale was already recorded.", duplicate = true, orderId = existing.Id, orderNumber = existing.OrderNumber,
                        receipt = await ReceiptFor(companyId.Value, existing.OrderNumber, existing.Subtotal, existing.Discount, existing.Total, existing.PaymentMethod) });
            }

            foreach (var item in dto.Items)
            {
                var product = await _context.Products.FindAsync(item.ProductId);
                if (product == null) return BadRequest(new { message = $"Product ID {item.ProductId} not found." });
                if (!dto.Offline && product.StockQuantity < item.Quantity)
                    return BadRequest(new { message = $"Insufficient stock for '{product.Name}'. Available: {product.StockQuantity}" });

                var unitPrice = (bargainAllowed && item.Price.HasValue && item.Price.Value > 0) ? item.Price.Value : product.Price;
                subtotal += unitPrice * item.Quantity;
                productIds.Add(item.ProductId);
                quantities.Add(item.Quantity);
                prices.Add(unitPrice);
            }

            if (dto.Discount > subtotal)
                return BadRequest(new { message = "Discount cannot exceed the subtotal." });

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

            if (clientRef.Length > 0 || dto.SoldAt.HasValue)
            {
                // Report the sale on the day it really happened (clamped: never in the future, never older than 30 days).
                var now = DateTime.UtcNow;
                var soldAt = dto.SoldAt.HasValue ? dto.SoldAt.Value.ToUniversalTime() : now;
                if (soldAt > now) soldAt = now;
                if (soldAt < now.AddDays(-30)) soldAt = now.AddDays(-30);
                await _context.Database.ExecuteSqlRawAsync(
                    "UPDATE orders SET client_ref = {0}, created_date = {1}, order_notes = {2} WHERE id = {3}",
                    clientRef.Length > 0 ? clientRef : (string?)null, soldAt,
                    dto.Offline ? "Sold offline; synced later." : (string?)null, orderId);
            }
            if (tx != null) await tx.CommitAsync();

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
