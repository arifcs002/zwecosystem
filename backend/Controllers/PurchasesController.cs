using Ecommerce.Api.Domain;
using Ecommerce.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Api.Controllers
{
    public record PoLineDto(int ProductId, int Quantity, decimal UnitCost);
    public record PoCreateDto(int SupplierId, List<PoLineDto> Items, string? Note);
    public record PoReceiveLineDto(int ItemId, int Quantity, DateTime? ExpiryDate = null, string? BatchNo = null);
    public record PoReceiveDto(List<PoReceiveLineDto> Lines, decimal? PaidNow, string? Method, bool UpdateCost = false);
    public record SupplierPaymentDto(int SupplierId, decimal Amount, string Method, string? Note);
    public record SupplierAdjustDto(int SupplierId, decimal Amount, string Note);

    // Ordering stock from a supplier and receiving it. Stock goes up only when goods are RECEIVED, and the value of
    // what was received is added to what the shop owes that supplier (see SupplierAccountsController).
    [ApiController]
    [Route("api/purchases")]
    [Authorize]
    public class PurchasesController : ControllerBase
    {
        private static readonly string[] Methods = { "cash", "bkash", "nagad", "bank", "other" };
        private readonly ApplicationDbContext _context;
        public PurchasesController(ApplicationDbContext context) { _context = context; }

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] int? supplierId, [FromQuery] string? status)
        {
            var q = _context.PurchaseOrders.AsNoTracking().AsQueryable();
            if (supplierId.HasValue) q = q.Where(p => p.SupplierId == supplierId.Value);
            if (!string.IsNullOrWhiteSpace(status)) q = q.Where(p => p.Status == status.ToUpper());
            var rows = await q.OrderByDescending(p => p.Id).Take(200).Select(p => new
            {
                p.Id, p.PoNumber, p.Status, p.SupplierId, p.CreatedDate, p.ReceivedDate, p.Note,
                supplier = _context.Suppliers.Where(s => s.Id == p.SupplierId).Select(s => s.Name).FirstOrDefault(),
                orderedValue = p.Items.Sum(i => i.Quantity * i.UnitCost),
                receivedValue = p.Items.Sum(i => i.ReceivedQty * i.UnitCost)
            }).ToListAsync();
            return Ok(rows);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> Get(int id)
        {
            var po = await _context.PurchaseOrders.AsNoTracking().Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == id);
            if (po == null) return NotFound();
            var names = await _context.Products.IgnoreQueryFilters().AsNoTracking()
                .Where(p => p.CompanyId == po.CompanyId && po.Items.Select(i => i.ProductId).Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name);
            return Ok(new
            {
                po.Id, po.PoNumber, po.Status, po.SupplierId, po.Note, po.CreatedDate, po.ReceivedDate,
                supplier = await _context.Suppliers.AsNoTracking().Where(s => s.Id == po.SupplierId).Select(s => s.Name).FirstOrDefaultAsync(),
                items = po.Items.Select(i => new { i.Id, i.ProductId, name = names.GetValueOrDefault(i.ProductId, "Item"), i.Quantity, i.UnitCost, i.ReceivedQty, remaining = i.Quantity - i.ReceivedQty })
            });
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] PoCreateDto dto)
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest(new { message = "Company context is required." });
            if (dto.Items == null || dto.Items.Count == 0) return BadRequest(new { message = "Add at least one item." });
            if (dto.Items.Count > 200) return BadRequest(new { message = "At most 200 items per order." });
            if (dto.Items.Any(i => i.Quantity <= 0 || i.Quantity > 100000)) return BadRequest(new { message = "Quantity must be between 1 and 100000." });
            if (dto.Items.Any(i => i.UnitCost < 0 || i.UnitCost > 10_000_000m)) return BadRequest(new { message = "Unit cost is not valid." });
            if (dto.Items.GroupBy(i => i.ProductId).Any(g => g.Count() > 1)) return BadRequest(new { message = "Each product can be listed once." });
            if (!await _context.Suppliers.AnyAsync(s => s.Id == dto.SupplierId)) return BadRequest(new { message = "Choose a supplier." });
            var ids = dto.Items.Select(i => i.ProductId).ToList();
            if (await _context.Products.CountAsync(p => ids.Contains(p.Id)) != ids.Count) return BadRequest(new { message = "One of the products was not found." });

            var po = new PurchaseOrder
            {
                CompanyId = companyId.Value, SupplierId = dto.SupplierId, PoNumber = $"PO-{DateTime.UtcNow:yyMMdd}-{Random.Shared.Next(1000, 9999)}",
                Note = Clip(dto.Note, 300), CreatedBy = _context.CurrentUserId,
                Items = dto.Items.Select(i => new PurchaseOrderItem { ProductId = i.ProductId, Quantity = i.Quantity, UnitCost = i.UnitCost }).ToList()
            };
            _context.PurchaseOrders.Add(po);
            try { await _context.SaveChangesAsync(); }
            catch (DbUpdateException) { po.PoNumber += "-" + Random.Shared.Next(10, 99); await _context.SaveChangesAsync(); }   // number clash: extremely rare
            await Audit.LogAsync(_context, HttpContext, "purchase.create", po.PoNumber);
            return Ok(new { po.Id, po.PoNumber, po.Status });
        }

        [HttpPost("{id}/cancel")]
        public async Task<IActionResult> Cancel(int id)
        {
            var po = await _context.PurchaseOrders.Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == id);
            if (po == null) return NotFound();
            if (po.Status != "ORDERED" || po.Items.Any(i => i.ReceivedQty > 0))
                return Conflict(new { message = "Only an order with nothing received yet can be cancelled." });
            po.Status = "CANCELLED";
            await _context.SaveChangesAsync();
            await Audit.LogAsync(_context, HttpContext, "purchase.cancel", po.PoNumber);
            return Ok(new { po.Id, po.Status });
        }

        [HttpPost("{id}/receive")]
        public async Task<IActionResult> Receive(int id, [FromBody] PoReceiveDto dto)
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest(new { message = "Company context is required." });
            if (dto.Lines == null || dto.Lines.Count == 0 || dto.Lines.Any(l => l.Quantity <= 0)) return BadRequest(new { message = "Enter the quantity received for at least one item." });
            if (dto.Lines.GroupBy(l => l.ItemId).Any(g => g.Count() > 1)) return BadRequest(new { message = "Each item can be listed once." });
            var method = (dto.Method ?? "cash").Trim().ToLowerInvariant();
            if (!Methods.Contains(method)) return BadRequest(new { message = "Method must be cash, bkash, nagad, bank or other." });
            if (dto.PaidNow is < 0) return BadRequest(new { message = "Amount paid cannot be negative." });

            await using var tx = await _context.Database.BeginTransactionAsync();
            await _context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtext({0}))", $"po:{companyId.Value}:{id}");   // two people receiving at once can't double-count
            var po = await _context.PurchaseOrders.Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == id);
            if (po == null) return NotFound();
            if (po.Status is "RECEIVED" or "CANCELLED") return Conflict(new { message = $"This order is already {po.Status.ToLower()}." });

            decimal value = 0;
            foreach (var l in dto.Lines)
            {
                var item = po.Items.FirstOrDefault(i => i.Id == l.ItemId);
                if (item == null) return BadRequest(new { message = "One of the items is not on this order." });
                var remaining = item.Quantity - item.ReceivedQty;
                if (l.Quantity > remaining) return BadRequest(new { message = $"Only {remaining} more of that item is expected." });

                await _context.Database.SqlQueryRaw<int>("SELECT sp_inventory_move({0},{1},{2},{3},{4},{5},{6},{7})",
                    companyId.Value, item.ProductId, l.Quantity, "PURCHASE", $"Purchase {po.PoNumber}", item.UnitCost, po.PoNumber, _context.CurrentUserId ?? 0).ToListAsync();
                item.ReceivedQty += l.Quantity;
                if (l.ExpiryDate != null)
                    await _context.Database.ExecuteSqlRawAsync("INSERT INTO product_batches (company_id, product_id, batch_no, expiry_date, quantity, received_qty) VALUES ({0},{1},{2}::text,{3}::date,{4},{4})", companyId.Value, item.ProductId, Clip(l.BatchNo, 40) ?? po.PoNumber, l.ExpiryDate.Value.Date.D(), l.Quantity);
                value += l.Quantity * item.UnitCost;
                if (dto.UpdateCost && item.UnitCost > 0)      // opt-in: it changes the cost used by profit reports
                    await _context.Database.ExecuteSqlRawAsync("UPDATE products SET wholesale_price = {0} WHERE id = {1} AND company_id = {2}", item.UnitCost, item.ProductId, companyId.Value);
            }
            value = Math.Round(value, 2);
            var paid = Math.Round(dto.PaidNow ?? 0, 2);
            if (paid > value) return BadRequest(new { message = "The amount paid now can't be more than the value received. Record extra as a payment." });

            po.Status = po.Items.All(i => i.ReceivedQty >= i.Quantity) ? "RECEIVED" : "PARTIAL";
            if (po.Status == "RECEIVED") po.ReceivedDate = DateTime.UtcNow;
            _context.SupplierLedger.Add(new SupplierLedgerEntry { CompanyId = companyId.Value, SupplierId = po.SupplierId, EntryType = "PURCHASE", Amount = value, PurchaseOrderId = po.Id, Note = $"Received on {po.PoNumber}", RecordedBy = _context.CurrentUserId });
            if (paid > 0)
                _context.SupplierLedger.Add(new SupplierLedgerEntry { CompanyId = companyId.Value, SupplierId = po.SupplierId, EntryType = "PAYMENT", Amount = -paid, PurchaseOrderId = po.Id, Method = method, Note = "Paid when received", RecordedBy = _context.CurrentUserId });
            await _context.SaveChangesAsync();
            await tx.CommitAsync();
            await Audit.LogAsync(_context, HttpContext, "purchase.receive", $"{po.PoNumber}: value {value}, paid {paid}");
            var balance = await _context.SupplierLedger.AsNoTracking().Where(e => e.SupplierId == po.SupplierId).SumAsync(e => (decimal?)e.Amount) ?? 0m;
            return Ok(new { po.Id, po.Status, receivedValue = value, paidNow = paid, owedToSupplier = balance });
        }

        private static string? Clip(string? s, int max) { if (string.IsNullOrWhiteSpace(s)) return null; s = s.Trim(); return s.Length > max ? s[..max] : s; }
    }

    // The supplier account: what the shop owes each supplier, with the full history. Append-only.
    [ApiController]
    [Route("api/supplier-accounts")]
    [Authorize]
    public class SupplierAccountsController : ControllerBase
    {
        private static readonly string[] Methods = { "cash", "bkash", "nagad", "bank", "other" };
        private readonly ApplicationDbContext _context;
        public SupplierAccountsController(ApplicationDbContext context) { _context = context; }

        [HttpGet]
        public async Task<IActionResult> List()
        {
            var suppliers = await _context.Suppliers.AsNoTracking().Select(s => new { s.Id, s.Name, s.PhoneNumber }).ToListAsync();
            var balances = await _context.SupplierLedger.AsNoTracking().GroupBy(e => e.SupplierId)
                .Select(g => new { id = g.Key, balance = g.Sum(e => e.Amount), last = g.Max(e => e.CreatedDate) }).ToDictionaryAsync(x => x.id);
            return Ok(suppliers.Select(s => new
            {
                s.Id, s.Name, s.PhoneNumber,
                balance = balances.TryGetValue(s.Id, out var b) ? b.balance : 0m,
                lastActivity = balances.TryGetValue(s.Id, out var b2) ? (DateTime?)b2.last : null
            }).OrderByDescending(x => x.balance).ThenBy(x => x.Name));
        }

        [HttpGet("summary")]
        public async Task<IActionResult> Summary()
        {
            var balances = await _context.SupplierLedger.AsNoTracking().GroupBy(e => e.SupplierId).Select(g => g.Sum(e => e.Amount)).ToListAsync();
            return Ok(new { totalOwed = balances.Where(b => b > 0).Sum(), suppliersOwed = balances.Count(b => b > 0), advancePaid = -balances.Where(b => b < 0).Sum() });
        }

        [HttpGet("{supplierId}")]
        public async Task<IActionResult> Detail(int supplierId)
        {
            var s = await _context.Suppliers.AsNoTracking().Where(x => x.Id == supplierId).Select(x => new { x.Id, x.Name, x.PhoneNumber }).FirstOrDefaultAsync();
            if (s == null) return NotFound();
            var entries = await _context.SupplierLedger.AsNoTracking().Where(e => e.SupplierId == supplierId).OrderByDescending(e => e.Id).Take(200)
                .Select(e => new { e.Id, e.EntryType, e.Amount, e.PurchaseOrderId, e.Method, e.Note, e.CreatedDate }).ToListAsync();
            var balance = await _context.SupplierLedger.AsNoTracking().Where(e => e.SupplierId == supplierId).SumAsync(e => (decimal?)e.Amount) ?? 0m;
            return Ok(new { supplier = s, balance, entries });
        }

        [HttpPost("payments")]
        public async Task<IActionResult> Pay([FromBody] SupplierPaymentDto dto)
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest(new { message = "Company context is required." });
            if (dto.Amount <= 0 || dto.Amount > 10_000_000m) return BadRequest(new { message = "Enter an amount greater than zero." });
            var method = (dto.Method ?? "cash").Trim().ToLowerInvariant();
            if (!Methods.Contains(method)) return BadRequest(new { message = "Method must be cash, bkash, nagad, bank or other." });
            if (!await _context.Suppliers.AnyAsync(x => x.Id == dto.SupplierId)) return NotFound(new { message = "Supplier not found." });

            await using var tx = await _context.Database.BeginTransactionAsync();
            await _context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtext({0}))", $"supplier:{companyId.Value}:{dto.SupplierId}");
            var before = await _context.SupplierLedger.Where(e => e.SupplierId == dto.SupplierId).SumAsync(e => (decimal?)e.Amount) ?? 0m;
            _context.SupplierLedger.Add(new SupplierLedgerEntry { CompanyId = companyId.Value, SupplierId = dto.SupplierId, EntryType = "PAYMENT", Amount = -dto.Amount, Method = method, Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim()[..Math.Min(300, dto.Note.Trim().Length)], RecordedBy = _context.CurrentUserId });
            await _context.SaveChangesAsync();
            await tx.CommitAsync();
            await Audit.LogAsync(_context, HttpContext, "supplier.payment", $"supplier {dto.SupplierId}: paid {dto.Amount} via {method}");
            return Ok(new { previousBalance = before, balance = before - dto.Amount, paid = dto.Amount });
        }

        [HttpPost("adjustments")]
        public async Task<IActionResult> Adjust([FromBody] SupplierAdjustDto dto)
        {
            if (!User.IsInRole("companyadmin") && !User.IsInRole("superadmin")) return Forbid();
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest(new { message = "Company context is required." });
            if (dto.Amount == 0 || Math.Abs(dto.Amount) > 10_000_000m) return BadRequest(new { message = "Enter a non-zero amount." });
            if (string.IsNullOrWhiteSpace(dto.Note)) return BadRequest(new { message = "Say why you are adjusting this account." });
            if (!await _context.Suppliers.AnyAsync(x => x.Id == dto.SupplierId)) return NotFound(new { message = "Supplier not found." });
            _context.SupplierLedger.Add(new SupplierLedgerEntry { CompanyId = companyId.Value, SupplierId = dto.SupplierId, EntryType = "ADJUST", Amount = dto.Amount, Note = dto.Note.Trim()[..Math.Min(300, dto.Note.Trim().Length)], RecordedBy = _context.CurrentUserId });
            await _context.SaveChangesAsync();
            await Audit.LogAsync(_context, HttpContext, "supplier.adjust", $"supplier {dto.SupplierId}: {dto.Amount:+0.##;-0.##} ({dto.Note})");
            var balance = await _context.SupplierLedger.AsNoTracking().Where(e => e.SupplierId == dto.SupplierId).SumAsync(e => (decimal?)e.Amount) ?? 0m;
            return Ok(new { balance });
        }
    }
}
