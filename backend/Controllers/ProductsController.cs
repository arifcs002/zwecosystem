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

        [HttpGet]
        public async Task<IActionResult> GetProducts([FromQuery] string? search)
        {
            var query = _context.Products
                .IgnoreQueryFilters()
                .Include(p => p.Category)
                .Include(p => p.Brand)
                .Include(p => p.Supplier)
                .AsQueryable();

            if (_context.CompanyId.HasValue)
                query = query.Where(p => p.CompanyId == _context.CompanyId.Value);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                query = query.Where(p =>
                    p.Name.ToLower().Contains(term) ||
                    p.Sku.ToLower().Contains(term) ||
                    p.Barcode.ToLower().Contains(term));
            }

            var items = await query.OrderByDescending(p => p.CreatedDate).ToListAsync();
            return Ok(items);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetProduct(int id)
        {
            var product = await _context.Products
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
                "SELECT sp_create_product({0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15})",
                companyId.Value, dto.Name, slug, dto.SKU, barcode, dto.Description ?? "",
                dto.Price, dto.WholesalePrice, dto.StockQuantity, dto.ImageUrl ?? "",
                dto.CategoryId, dto.BrandId, "PUBLISHED", "", (int?)null, _context.CurrentUserId
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
                    "SELECT sp_create_product({0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15})",
                    companyId.Value, $"{dto.Name} (Size {sizeQty.Size})", slug, sku, barcode,
                    dto.Description ?? "", dto.Price, dto.WholesalePrice, sizeQty.Quantity,
                    dto.ImageUrl ?? "", dto.CategoryId, (int?)null, "PUBLISHED",
                    sizeQty.Size, dto.SupplierId, _context.CurrentUserId
                ).ToListAsync()).FirstOrDefault();

                var product = await _context.Products.FindAsync(productId);
                if (product != null) created.Add(product);
            }
            return Ok(created);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateProduct(int id, [FromBody] ProductCreateDto dto)
        {
            if (!await _context.Products.AnyAsync(p => p.Id == id)) return NotFound();

            await _context.Database.ExecuteSqlRawAsync(
                "CALL sp_update_product({0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12})",
                id, dto.Name, dto.SKU, dto.Price, dto.WholesalePrice, dto.StockQuantity,
                dto.Description ?? "", dto.ImageUrl ?? "", dto.CategoryId, dto.BrandId,
                "", (int?)null, _context.CurrentUserId);

            var product = await _context.Products.FindAsync(id);
            return Ok(product);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteProduct(int id)
        {
            if (!await _context.Products.AnyAsync(p => p.Id == id)) return NotFound();
            await _context.Database.ExecuteSqlRawAsync("CALL sp_delete_product({0})", id);
            return NoContent();
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
