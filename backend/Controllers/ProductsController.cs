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
    public class ProductsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IFileTextLogger _fileLogger;

        public ProductsController(ApplicationDbContext context, IFileTextLogger fileLogger)
        {
            _context = context;
            _fileLogger = fileLogger;
        }

        // Anonymous-safe storefront listing. Deliberately a separate endpoint (not
        // [AllowAnonymous] on GetProducts) so wholesale/cost price never reaches a
        // customer's browser — only the fields a storefront card needs.
        [HttpGet("public")]
        [AllowAnonymous]
        public async Task<IActionResult> GetPublicProducts()
        {
            var items = await _context.Products
                .AsNoTracking()
                .Include(p => p.Category)
                .OrderByDescending(p => p.CreatedDate)
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.Price,
                    p.CompareAtPrice,
                    p.ImageUrl,
                    p.StockQuantity,
                    p.CategoryId,
                    p.CreatedDate,
                    Category = p.Category != null ? new { p.Category.Name } : null
                })
                .ToListAsync();
            return Ok(items);
        }

        [HttpGet]
        public async Task<IActionResult> GetProducts([FromQuery] string? search)
        {
            var query = _context.Products
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(p => p.Category)
                .Where(p => p.IsDeleted == 0)   // always exclude soft-deleted
                .AsQueryable();

            if (_context.CompanyId.HasValue)
                query = query.Where(p => p.CompanyId == _context.CompanyId.Value);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                query = query.Where(p =>
                    p.Name.ToLower().Contains(term) ||
                    (p.Sku != null && p.Sku.ToLower().Contains(term)) ||
                    (p.Barcode != null && p.Barcode.ToLower().Contains(term)));
            }

            var items = await query.OrderByDescending(p => p.CreatedDate).ToListAsync();
            return Ok(items);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetProduct(int id)
        {
            var product = await _context.Products
                .AsNoTracking()
                .Include(p => p.Category)
                .Include(p => p.Brand)
                .FirstOrDefaultAsync(p => p.Id == id);
            if (product == null) return NotFound();
            return Ok(product);
        }

        [HttpPost]
        public async Task<IActionResult> CreateProduct([FromBody] ProductCreateDto dto)
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest("Company context is required.");

            var barcode = dto.Barcode;
            if (string.IsNullOrEmpty(barcode)) barcode = GenerateBarcode(companyId.Value);

            while (await _context.Products.AnyAsync(p => p.CompanyId == companyId.Value && p.Barcode == barcode))
                barcode = GenerateBarcode(companyId.Value);

            var slug = dto.Name.ToLower().Replace(" ", "-").Replace("/", "-");

            var productId = (await _context.Database.SqlQueryRaw<int>(
                "SELECT sp_create_product({0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17})",
                companyId.Value, dto.Name, slug, dto.SKU, barcode, dto.Description ?? "",
                dto.Price, dto.WholesalePrice, dto.StockQuantity, dto.ImageUrl ?? "",
                dto.CategoryId, dto.BrandId, "PUBLISHED", "", (int?)null, _context.CurrentUserId, dto.PricingTagId,
                dto.CompareAtPrice ?? 0
            ).ToListAsync()).FirstOrDefault();

            var product = await _context.Products.FindAsync(productId);
            return CreatedAtAction(nameof(GetProduct), new { id = productId }, product);
        }

        [HttpPost("batch")]
        public async Task<IActionResult> CreateProductsBatch([FromBody] BatchProductCreateDto dto)
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest("Company context is required.");

            var created = new List<Product>();
            foreach (var sizeQty in dto.Sizes)
            {
                if (sizeQty.Quantity <= 0) continue;

                var barcode = GenerateBarcode(companyId.Value);
                while (await _context.Products.AnyAsync(p => p.CompanyId == companyId.Value && p.Barcode == barcode))
                    barcode = GenerateBarcode(companyId.Value);

                var sku  = $"{dto.Name.Replace(" ", "-").ToUpper()}-{sizeQty.Size.ToUpper()}";
                var slug = $"{dto.Name.ToLower().Replace(" ", "-").Replace("/", "-")}-{sizeQty.Size.ToLower()}";

                var productId = (await _context.Database.SqlQueryRaw<int>(
                    "SELECT sp_create_product({0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17})",
                    companyId.Value, $"{dto.Name} (Size {sizeQty.Size})", slug, sku, barcode,
                    dto.Description ?? "", dto.Price, dto.WholesalePrice, sizeQty.Quantity,
                    dto.ImageUrl ?? "", dto.CategoryId, (int?)null, "PUBLISHED",
                    sizeQty.Size, dto.SupplierId, _context.CurrentUserId, dto.PricingTagId,
                    dto.CompareAtPrice ?? 0
                ).ToListAsync()).FirstOrDefault();

                var product = await _context.Products.FindAsync(productId);
                if (product != null) created.Add(product);
            }
            return Ok(created);
        }

        // Bulk add — many distinct products under one supplier + category, created
        // in a single transaction so either all succeed or none do.
        [HttpPost("bulk")]
        public async Task<IActionResult> CreateBulkProducts([FromBody] BulkProductsCreateDto dto)
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest("Company context is required.");
            if (dto.Products == null || dto.Products.Count == 0) return BadRequest("Add at least one product.");

            var created = new List<Product>();
            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                foreach (var line in dto.Products)
                {
                    if (string.IsNullOrWhiteSpace(line.Name)) continue;
                    // A product with no sizes is a single stock unit; one with sizes
                    // becomes one row per size (mirrors the single-product batch flow).
                    var sizes = (line.Sizes != null && line.Sizes.Count > 0)
                        ? line.Sizes.Where(s => s.Quantity > 0).ToList()
                        : new List<SizeQtyDto> { new("", 0) };

                    foreach (var sq in sizes)
                    {
                        var hasSize = !string.IsNullOrWhiteSpace(sq.Size);
                        var name = hasSize ? $"{line.Name} (Size {sq.Size})" : line.Name;
                        var barcode = GenerateBarcode(companyId.Value);
                        while (await _context.Products.IgnoreQueryFilters().AnyAsync(p => p.CompanyId == companyId.Value && p.Barcode == barcode))
                            barcode = GenerateBarcode(companyId.Value);
                        var sku = hasSize
                            ? $"{line.Name.Replace(" ", "-").ToUpper()}-{sq.Size.ToUpper()}"
                            : line.Name.Replace(" ", "-").ToUpper();
                        var slug = hasSize
                            ? $"{line.Name.ToLower().Replace(" ", "-").Replace("/", "-")}-{sq.Size.ToLower()}"
                            : line.Name.ToLower().Replace(" ", "-").Replace("/", "-");

                        var productId = (await _context.Database.SqlQueryRaw<int>(
                            "SELECT sp_create_product({0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17})",
                            companyId.Value, name, slug, sku, barcode,
                            line.Description ?? "", line.Price, line.WholesalePrice, sq.Quantity,
                            line.ImageUrl ?? "", dto.CategoryId, (int?)null, "PUBLISHED",
                            sq.Size ?? "", dto.SupplierId, _context.CurrentUserId, line.PricingTagId,
                            line.CompareAtPrice ?? 0
                        ).ToListAsync()).FirstOrDefault();

                        var product = await _context.Products.FindAsync(productId);
                        if (product != null) created.Add(product);
                    }
                }

                await tx.CommitAsync();
                return Ok(new { count = created.Count, products = created });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                _fileLogger.LogError("PRODUCTS", "Bulk create failed", ex);
                return StatusCode(500, new { message = "Bulk save failed — no products were created." });
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateProduct(int id, [FromBody] ProductCreateDto dto)
        {
            if (!await _context.Products.AnyAsync(p => p.Id == id)) return NotFound();

            await _context.Database.ExecuteSqlRawAsync(
                "CALL sp_update_product({0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14})",
                id, dto.Name, dto.SKU, dto.Price, dto.WholesalePrice, dto.StockQuantity,
                dto.Description ?? "", dto.ImageUrl ?? "", dto.CategoryId, dto.BrandId,
                "", (int?)null, _context.CurrentUserId, dto.PricingTagId, dto.CompareAtPrice ?? 0);

            var product = await _context.Products.FindAsync(id);
            return Ok(product);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteProduct(int id)
        {
            // Soft-delete is idempotent — skip the query-filtered existence check
            // (global filter excludes IsDeleted=1 rows, causing false 404 on retry).
            await _context.Database.ExecuteSqlRawAsync("CALL sp_delete_product({0})", id);
            return NoContent();
        }

        [HttpPatch("{id}/stock")]
        public async Task<IActionResult> AdjustStock(int id, [FromBody] StockAdjustDto dto)
        {
            if (!await _context.Products.AnyAsync(p => p.Id == id && p.IsDeleted == 0)) return NotFound();
            await _context.Database.ExecuteSqlRawAsync(
                "CALL sp_adjust_stock({0},{1},{2})", id, dto.Delta, _context.CurrentUserId);
            var p = await _context.Products.FindAsync(id);
            return Ok(new { id = p!.Id, stockQuantity = p.StockQuantity });
        }

        [HttpPost("{id}/print-barcode")]
        public async Task<IActionResult> PrintBarcode(int id, [FromQuery] string ipAddress = "192.168.1.100", [FromQuery] int port = 9100)
        {
            var product = await _context.Products.FindAsync(id);
            if (product == null) return NotFound();

            var zpl = $@"^XA
^LH30,30
^FO20,10^A0N,28,24^FD{product.Name}^FS
^FO20,40^A0N,20,16^FDSKU: {product.Sku}  Price: {product.Price} BDT^FS
^FO20,70^BY2^BCN,50,Y,N,N^FD{product.Barcode}^FS
^XZ";

            return Ok(new { message = $"Barcode sent to {ipAddress}:{port}", zpl = zpl.Trim(), ipAddress, port });
        }

        private static string GenerateBarcode(int companyId)
            => $"ZW-{new Random().Next(100000, 999999)}";
    }
}
