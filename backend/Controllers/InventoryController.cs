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
    public class InventoryController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public InventoryController(ApplicationDbContext context)
        {
            _context = context;
        }

        // Stock overview for the current company (or all, for a super admin with
        // no company selected). Includes the low-stock threshold + a rollup so the
        // UI can flag low/out-of-stock rows and show total stock value.
        [HttpGet]
        public async Task<IActionResult> GetOverview()
        {
            var items = await _context.Products
                .AsNoTracking()
                .Include(p => p.Category)
                .OrderBy(p => p.Name)
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.Sku,
                    p.Size,
                    p.StockQuantity,
                    p.Price,
                    p.WholesalePrice,
                    p.ImageUrl,
                    Category = p.Category != null ? p.Category.Name : null
                })
                .ToListAsync();

            var thresholdStr = await _context.CompanySettings
                .Where(s => s.Key == "low_stock_threshold")
                .Select(s => s.Value)
                .FirstOrDefaultAsync();
            int threshold = int.TryParse(thresholdStr, out var t) ? t : 5;

            var totalUnits = items.Sum(i => i.StockQuantity);
            var stockValue = items.Sum(i => i.StockQuantity * i.WholesalePrice);
            var lowCount = items.Count(i => i.StockQuantity > 0 && i.StockQuantity <= threshold);
            var outCount = items.Count(i => i.StockQuantity <= 0);

            return Ok(new
            {
                lowStockThreshold = threshold,
                summary = new { skuCount = items.Count, totalUnits, stockValue, lowCount, outCount },
                items
            });
        }

        [HttpPost("adjust")]
        public async Task<IActionResult> Adjust([FromBody] InventoryAdjustDto dto)
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest(new { message = "Select a company first." });
            if (dto.Delta == 0) return BadRequest(new { message = "Enter a non-zero quantity." });

            var type = dto.Delta > 0 ? "ADJUST_IN" : "ADJUST_OUT";
            var newStock = (await _context.Database.SqlQueryRaw<int>(
                "SELECT sp_inventory_move({0},{1},{2},{3},{4},{5},{6},{7})",
                companyId.Value, dto.ProductId, dto.Delta, type,
                dto.Reason ?? "", 0m, "", _context.CurrentUserId ?? 0
            ).ToListAsync()).FirstOrDefault();

            return Ok(new { productId = dto.ProductId, stockQuantity = newStock });
        }

        [HttpPost("purchase")]
        public async Task<IActionResult> Purchase([FromBody] InventoryPurchaseDto dto)
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest(new { message = "Select a company first." });
            if (dto.Quantity <= 0) return BadRequest(new { message = "Quantity must be greater than zero." });

            var reference = string.IsNullOrWhiteSpace(dto.Supplier) ? "" : $"Supplier: {dto.Supplier}";
            var newStock = (await _context.Database.SqlQueryRaw<int>(
                "SELECT sp_inventory_move({0},{1},{2},{3},{4},{5},{6},{7})",
                companyId.Value, dto.ProductId, dto.Quantity, "PURCHASE",
                dto.Reason ?? "", dto.UnitCost ?? 0m, reference, _context.CurrentUserId ?? 0
            ).ToListAsync()).FirstOrDefault();

            return Ok(new { productId = dto.ProductId, stockQuantity = newStock });
        }

        [HttpGet("movements")]
        public async Task<IActionResult> GetMovements([FromQuery] int? productId, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            var companyId = _context.CompanyId ?? 0;
            var movements = await _context.Database
                .SqlQueryRaw<InventoryMovementVm>(
                    "SELECT * FROM sp_get_inventory_movements({0},{1},{2},{3})",
                    companyId,
                    (object?)productId ?? DBNull.Value,
                    (object?)from ?? DBNull.Value,
                    (object?)to ?? DBNull.Value)
                .ToListAsync();
            return Ok(movements);
        }
    }
}
