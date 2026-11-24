using Ecommerce.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Api.Controllers
{
    public record BatchCreateDto(int ProductId, string? BatchNo, DateTime ExpiryDate, int Quantity);
    public record BatchWriteOffDto(string? Note);

    // Expiry dates for food, medicine, cosmetics...: see what is about to expire, and write off what already has.
    [ApiController]
    [Route("api/batches")]
    [Authorize]
    public class BatchesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        public BatchesController(ApplicationDbContext context) { _context = context; }

        public sealed class BatchRow
        {
            public int Id { get; set; } public int ProductId { get; set; } public string ProductName { get; set; } = ""; public string? BatchNo { get; set; }
            public DateTime ExpiryDate { get; set; } public int Quantity { get; set; } public decimal ValueAtCost { get; set; } public int DaysLeft { get; set; }
        }
        public sealed class Claimed { public int ProductId { get; set; } public int Quantity { get; set; } }

        private const string Select = @"SELECT b.id AS ""Id"", b.product_id AS ""ProductId"", p.name AS ""ProductName"", b.batch_no AS ""BatchNo"", b.expiry_date AS ""ExpiryDate"", b.quantity AS ""Quantity"",
                  (b.quantity * p.wholesale_price) AS ""ValueAtCost"", (b.expiry_date - {1}::date) AS ""DaysLeft""
                  FROM product_batches b JOIN products p ON p.id = b.product_id WHERE b.company_id = {0} AND b.status = 'ACTIVE' AND b.quantity > 0";

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] int? productId)
        {
            var cid = _context.CompanyId;
            if (!cid.HasValue) return BadRequest(new { message = "Company context is required." });
            var rows = productId != null
                ? await _context.Database.SqlQueryRaw<BatchRow>(Select + " AND b.product_id = {2} ORDER BY b.expiry_date, b.id LIMIT 500", cid.Value, Batches.TodayBd().D(), productId.Value).ToListAsync()
                : await _context.Database.SqlQueryRaw<BatchRow>(Select + " ORDER BY b.expiry_date, b.id LIMIT 500", cid.Value, Batches.TodayBd().D()).ToListAsync();
            return Ok(rows);
        }

        // Already expired, and expiring within `days` days.
        [HttpGet("expiring")]
        public async Task<IActionResult> Expiring([FromQuery] int days = 30)
        {
            var cid = _context.CompanyId;
            if (!cid.HasValue) return BadRequest(new { message = "Company context is required." });
            days = Math.Clamp(days, 1, 365);
            var rows = await _context.Database.SqlQueryRaw<BatchRow>(Select + " AND b.expiry_date <= {1}::date + {2}::int ORDER BY b.expiry_date, b.id LIMIT 500", cid.Value, Batches.TodayBd().D(), days).ToListAsync();
            return Ok(new
            {
                expired = rows.Where(r => r.DaysLeft < 0),
                soon = rows.Where(r => r.DaysLeft >= 0),
                expiredValue = rows.Where(r => r.DaysLeft < 0).Sum(r => r.ValueAtCost),
                soonValue = rows.Where(r => r.DaysLeft >= 0).Sum(r => r.ValueAtCost)
            });
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] BatchCreateDto dto)
        {
            var cid = _context.CompanyId;
            if (!cid.HasValue) return BadRequest(new { message = "Company context is required." });
            if (dto.Quantity < 1 || dto.Quantity > 1_000_000) return BadRequest(new { message = "Enter how many units are in this lot." });
            if (dto.ExpiryDate.Date < Batches.TodayBd().AddYears(-1) || dto.ExpiryDate.Date > Batches.TodayBd().AddYears(15)) return BadRequest(new { message = "The expiry date does not look right." });
            if (!await _context.Products.AsNoTracking().AnyAsync(p => p.Id == dto.ProductId)) return NotFound(new { message = "Product not found." });
            await using var tx = await _context.Database.BeginTransactionAsync();
            await _context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtext({0}))", $"batch:{cid.Value}:{dto.ProductId}");
            var tagged = (await _context.Database.SqlQueryRaw<int>(@"SELECT COALESCE(SUM(quantity),0)::int AS ""Value"" FROM product_batches WHERE company_id={0} AND product_id={1} AND status='ACTIVE'", cid.Value, dto.ProductId).ToListAsync()).First();
            var stock = (await _context.Database.SqlQueryRaw<int>(@"SELECT stock_quantity AS ""Value"" FROM products WHERE id={0} AND company_id={1}", dto.ProductId, cid.Value).ToListAsync()).First();
            if (tagged + dto.Quantity > stock) return BadRequest(new { message = $"Only {Math.Max(stock - tagged, 0)} units of this product are not yet in a lot. Add stock first, or enter fewer." });
            var batchNo = string.IsNullOrWhiteSpace(dto.BatchNo) ? null : (dto.BatchNo.Trim().Length > 40 ? dto.BatchNo.Trim()[..40] : dto.BatchNo.Trim());
            var id = (await _context.Database.SqlQueryRaw<int>(
                @"INSERT INTO product_batches (company_id, product_id, batch_no, expiry_date, quantity, received_qty) VALUES ({0},{1},{2}::text,{3}::date,{4},{4}) RETURNING id AS ""Value""",
                cid.Value, dto.ProductId, batchNo, dto.ExpiryDate.Date.D(), dto.Quantity).ToListAsync()).First();
            await tx.CommitAsync();
            return Ok(new { id });
        }

        // Throw away what is left of a lot: stock goes down by the same amount (recorded in the stock movements).
        [HttpPost("{id:int}/writeoff")]
        public async Task<IActionResult> WriteOff(int id, [FromBody] BatchWriteOffDto dto)
        {
            var cid = _context.CompanyId;
            if (!cid.HasValue) return BadRequest(new { message = "Company context is required." });
            await using var tx = await _context.Database.BeginTransactionAsync();
            // atomic claim: two people clicking at once can only write the lot off once
            var claimed = (await _context.Database.SqlQueryRaw<Claimed>(
                @"WITH old AS (SELECT id, product_id, quantity FROM product_batches WHERE id = {0} AND company_id = {1} AND status = 'ACTIVE' AND quantity > 0 FOR UPDATE),
                       upd AS (UPDATE product_batches b SET quantity = 0, status = 'WRITTEN_OFF', note = {2}::text FROM old WHERE b.id = old.id RETURNING old.product_id, old.quantity)
                  SELECT product_id AS ""ProductId"", quantity AS ""Quantity"" FROM upd",
                id, cid.Value, string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim()).ToListAsync()).FirstOrDefault();
            if (claimed == null) return NotFound(new { message = "This lot is already written off or empty." });
            await _context.Database.SqlQueryRaw<int>("SELECT sp_inventory_move({0},{1},{2},{3},{4},{5},{6},{7})",
                cid.Value, claimed.ProductId, -claimed.Quantity, "WRITE_OFF", string.IsNullOrWhiteSpace(dto.Note) ? "Expired / damaged lot" : dto.Note.Trim(), 0m, $"batch-{id}", _context.CurrentUserId ?? 0).ToListAsync();
            await tx.CommitAsync();
            await Audit.LogAsync(_context, HttpContext, "batch.writeoff", $"lot #{id}: {claimed.Quantity} units");
            return Ok(new { writtenOff = claimed.Quantity });
        }
    }
}
