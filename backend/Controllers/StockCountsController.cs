using Ecommerce.Api.Domain;
using Ecommerce.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Api.Controllers
{
    public record StockCountCreateDto(string? Name, int? CategoryId, string? Note);
    public record CountLineDto(int ProductId, int? CountedQty);
    public record CountLinesDto(List<CountLineDto> Lines);
    public record CountScanDto(string Code, int Quantity = 1);

    // Counting the shelves and correcting the system. Starting a count freezes what the system EXPECTS for each product;
    // when applied, each product is adjusted by (counted − expected), so sales made while you were counting are not lost.
    [ApiController]
    [Route("api/stockcounts")]
    [Authorize]
    public class StockCountsController : ControllerBase
    {
        public const int MaxLines = 5000;
        private readonly ApplicationDbContext _context;
        public StockCountsController(ApplicationDbContext context) { _context = context; }

        [HttpGet]
        public async Task<IActionResult> List()
        {
            var rows = await _context.StockCounts.AsNoTracking().OrderByDescending(c => c.Id).Take(100).Select(c => new
            {
                c.Id, c.Name, c.Status, c.CreatedDate, c.AppliedDate,
                lines = c.Lines.Count, counted = c.Lines.Count(l => l.CountedQty != null),
                variances = c.Lines.Count(l => l.CountedQty != null && l.CountedQty != l.ExpectedQty)
            }).ToListAsync();
            return Ok(rows);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> Get(int id)
        {
            var c = await _context.StockCounts.AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);
            if (c == null) return NotFound();
            var ids = c.Lines.Select(l => l.ProductId).ToList();
            var products = await _context.Products.IgnoreQueryFilters().AsNoTracking().Where(p => p.CompanyId == c.CompanyId && ids.Contains(p.Id))
                .Select(p => new { p.Id, p.Name, p.Barcode, p.Sku, p.StockQuantity }).ToDictionaryAsync(p => p.Id);
            var lines = c.Lines.OrderBy(l => products.TryGetValue(l.ProductId, out var p) ? p.Name : "").Select(l =>
            {
                products.TryGetValue(l.ProductId, out var p);
                var variance = l.CountedQty.HasValue ? l.CountedQty.Value - l.ExpectedQty : (int?)null;
                return new
                {
                    l.ProductId, name = p?.Name ?? "Item", barcode = p?.Barcode, sku = p?.Sku,
                    expected = l.ExpectedQty, counted = l.CountedQty, variance, valueOfVariance = variance.HasValue ? variance.Value * l.UnitCost : (decimal?)null,
                    currentStock = p?.StockQuantity
                };
            }).ToList();
            return Ok(new
            {
                c.Id, c.Name, c.Status, c.Note, c.CreatedDate, c.AppliedDate,
                summary = new
                {
                    lines = lines.Count, counted = lines.Count(l => l.counted != null), notCounted = lines.Count(l => l.counted == null),
                    shortages = lines.Count(l => l.variance < 0), surpluses = lines.Count(l => l.variance > 0),
                    shortageValue = -lines.Where(l => l.variance < 0).Sum(l => l.valueOfVariance ?? 0),
                    surplusValue = lines.Where(l => l.variance > 0).Sum(l => l.valueOfVariance ?? 0)
                },
                lines
            });
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] StockCountCreateDto dto)
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest(new { message = "Company context is required." });
            if (await _context.StockCounts.CountAsync(c => c.Status == "OPEN") >= 3)
                return BadRequest(new { message = "You already have 3 counts in progress. Finish or cancel one first." });

            var q = _context.Products.AsNoTracking().AsQueryable();
            if (dto.CategoryId.HasValue) q = q.Where(p => p.CategoryId == dto.CategoryId.Value);
            var products = await q.Select(p => new { p.Id, p.StockQuantity, p.WholesalePrice }).ToListAsync();
            if (products.Count == 0) return BadRequest(new { message = "There are no products to count." });
            if (products.Count > MaxLines) return BadRequest(new { message = $"A count can hold at most {MaxLines} products. Count one category at a time." });

            var name = string.IsNullOrWhiteSpace(dto.Name) ? $"Count {DateTime.UtcNow.AddHours(6):d MMM yyyy}" : dto.Name.Trim()[..Math.Min(80, dto.Name.Trim().Length)];
            var count = new StockCount
            {
                CompanyId = companyId.Value, Name = name, Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim()[..Math.Min(300, dto.Note.Trim().Length)], CreatedBy = _context.CurrentUserId,
                Lines = products.Select(p => new StockCountLine { ProductId = p.Id, ExpectedQty = p.StockQuantity, UnitCost = p.WholesalePrice }).ToList()
            };
            _context.StockCounts.Add(count);
            await _context.SaveChangesAsync();
            await Audit.LogAsync(_context, HttpContext, "stockcount.create", $"{name}: {products.Count} products");
            return Ok(new { count.Id, count.Name, lines = products.Count });
        }

        // Type the quantities you counted. null clears a line back to "not counted".
        [HttpPut("{id}/lines")]
        public async Task<IActionResult> SetLines(int id, [FromBody] CountLinesDto dto)
        {
            if (dto.Lines == null || dto.Lines.Count == 0) return BadRequest(new { message = "Nothing to save." });
            if (dto.Lines.Any(l => l.CountedQty is < 0 or > 1_000_000)) return BadRequest(new { message = "Counted quantity must be between 0 and 1,000,000." });
            var c = await _context.StockCounts.FirstOrDefaultAsync(x => x.Id == id);
            if (c == null) return NotFound();
            if (c.Status != "OPEN") return Conflict(new { message = $"This count is already {c.Status.ToLower()}." });

            var lines = await _context.StockCountLines.Where(l => l.StockCountId == id).ToListAsync();
            foreach (var l in dto.Lines)
            {
                var line = lines.FirstOrDefault(x => x.ProductId == l.ProductId);
                if (line == null) return BadRequest(new { message = "One of the products is not part of this count." });
                line.CountedQty = l.CountedQty;
            }
            await _context.SaveChangesAsync();
            return Ok(new { saved = dto.Lines.Count });
        }

        // Scan a barcode: adds to that product's counted quantity (so scanning five pairs counts five).
        [HttpPost("{id}/scan")]
        public async Task<IActionResult> Scan(int id, [FromBody] CountScanDto dto)
        {
            var code = (dto.Code ?? "").Trim();
            if (code.Length == 0 || dto.Quantity is < 1 or > 1000) return BadRequest(new { message = "Scan a barcode." });
            var c = await _context.StockCounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (c == null) return NotFound();
            if (c.Status != "OPEN") return Conflict(new { message = $"This count is already {c.Status.ToLower()}." });

            var product = await _context.Products.AsNoTracking().Where(p => p.Barcode == code || p.Sku == code || p.UniqueCode == code).Select(p => new { p.Id, p.Name }).FirstOrDefaultAsync();
            if (product == null) return NotFound(new { message = $"No product matches “{code}”." });

            var affected = await _context.Database.ExecuteSqlRawAsync(
                "UPDATE stock_count_lines SET counted_qty = COALESCE(counted_qty, 0) + {0} WHERE stock_count_id = {1} AND product_id = {2}", dto.Quantity, id, product.Id);
            if (affected == 0) return NotFound(new { message = $"“{product.Name}” is not part of this count." });
            var now = await _context.StockCountLines.AsNoTracking().Where(l => l.StockCountId == id && l.ProductId == product.Id).Select(l => new { l.CountedQty, l.ExpectedQty }).FirstAsync();
            return Ok(new { productId = product.Id, product.Name, counted = now.CountedQty, expected = now.ExpectedQty });
        }

        [HttpPost("{id}/apply")]
        public async Task<IActionResult> Apply(int id)
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest(new { message = "Company context is required." });

            await using var tx = await _context.Database.BeginTransactionAsync();
            await _context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtext({0}))", $"stockcount:{companyId.Value}:{id}");
            // Claim the count atomically: only ONE request can flip OPEN -> APPLIED, however many arrive together.
            var claimed = await _context.Database.ExecuteSqlRawAsync(
                "UPDATE stock_counts SET status = 'APPLIED', applied_date = NOW() WHERE id = {0} AND company_id = {1} AND status = 'OPEN'", id, companyId.Value);
            var c = await _context.StockCounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (c == null) return NotFound();
            if (claimed == 0) return Conflict(new { message = $"This count is already {c.Status.ToLower()}." });

            var lines = await _context.StockCountLines.Where(l => l.StockCountId == id && l.CountedQty != null).ToListAsync();
            var adjusted = 0; decimal shortage = 0, surplus = 0;
            foreach (var l in lines)
            {
                var delta = l.CountedQty!.Value - l.ExpectedQty;
                if (delta == 0) continue;
                await _context.Database.SqlQueryRaw<int>("SELECT sp_inventory_move({0},{1},{2},{3},{4},{5},{6},{7})",
                    companyId.Value, l.ProductId, delta, "COUNT", $"Stock count: {c.Name}", l.UnitCost, $"COUNT-{c.Id}", _context.CurrentUserId ?? 0).ToListAsync();
                adjusted++;
                if (delta < 0) shortage += -delta * l.UnitCost; else surplus += delta * l.UnitCost;
            }
            await tx.CommitAsync();
            await Audit.LogAsync(_context, HttpContext, "stockcount.apply", $"{c.Name}: {adjusted} products adjusted (short {shortage}, over {surplus})");
            return Ok(new { c.Id, Status = "APPLIED", adjusted, shortageValue = shortage, surplusValue = surplus, notCounted = await _context.StockCountLines.CountAsync(l => l.StockCountId == id && l.CountedQty == null) });
        }

        [HttpPost("{id}/cancel")]
        public async Task<IActionResult> Cancel(int id)
        {
            var c = await _context.StockCounts.FirstOrDefaultAsync(x => x.Id == id);
            if (c == null) return NotFound();
            if (c.Status != "OPEN") return Conflict(new { message = $"This count is already {c.Status.ToLower()}." });
            c.Status = "CANCELLED";
            await _context.SaveChangesAsync();
            return Ok(new { c.Id, c.Status });
        }
    }
}
