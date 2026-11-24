using Ecommerce.Api.Domain;
using Ecommerce.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Api.Controllers
{
    public record ReturnLineDto(int OrderItemId, int Quantity, bool Restock = true);
    public record ExchangeLineDto(int ProductId, int Quantity);
    public record ReturnCreateDto(int OrderId, List<ReturnLineDto> Items, string RefundMethod, string? Reason, List<ExchangeLineDto>? ExchangeItems = null);

    // Customer brings goods back: stock goes back on the shelf (if the item is resellable), the customer is
    // paid back in cash or has it taken off what they owe, or swaps it for other goods.
    [ApiController]
    [Route("api/returns")]
    [Authorize]
    public class ReturnsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        public ReturnsController(ApplicationDbContext context) { _context = context; }

        // What can still be returned from an order (sold minus already returned), with each line's refund value.
        // Look an order up by the number printed on its receipt.
        [HttpGet("find")]
        public async Task<IActionResult> Find([FromQuery] string orderNumber)
        {
            var n = (orderNumber ?? "").Trim();
            if (n.Length < 3) return BadRequest(new { message = "Type the order number from the receipt." });
            var id = await _context.Orders.AsNoTracking().Where(o => o.OrderNumber == n).Select(o => (int?)o.Id).FirstOrDefaultAsync();
            if (id == null) return NotFound(new { message = "No order with that number." });
            return await Returnable(id.Value);
        }

        [HttpGet("order/{orderId}")]
        public async Task<IActionResult> Returnable(int orderId)
        {
            var order = await _context.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId);
            if (order == null) return NotFound();
            var factor = Factor(order);
            var items = await _context.OrderItems.AsNoTracking().Where(i => i.OrderId == orderId)
                .Select(i => new { i.Id, i.ProductId, Name = i.Product != null ? i.Product.Name : "Item", i.Quantity, i.Price }).ToListAsync();
            var returned = await ReturnedQuantities(orderId);
            return Ok(new
            {
                order = new { order.Id, order.OrderNumber, order.Status, order.Total, order.PaymentMethod, order.CustomerName, order.CustomerPhone },
                items = items.Select(i => new
                {
                    orderItemId = i.Id, i.ProductId, i.Name, sold = i.Quantity, returned = returned.GetValueOrDefault(i.Id),
                    remaining = i.Quantity - returned.GetValueOrDefault(i.Id), unitPrice = i.Price,
                    refundPerUnit = Math.Round(i.Price * factor, 2)
                })
            });
        }

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] int? orderId)
        {
            var q = _context.OrderReturns.AsNoTracking().AsQueryable();
            if (orderId.HasValue) q = q.Where(r => r.OrderId == orderId.Value);
            var rows = await q.OrderByDescending(r => r.Id).Take(200)
                .Select(r => new { r.Id, r.ReturnNumber, r.OrderId, r.Kind, r.RefundAmount, r.RefundMethod, r.CashRefunded, r.Reason, r.NewOrderId, r.CreatedDate }).ToListAsync();
            return Ok(rows);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] ReturnCreateDto dto)
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest(new { message = "Company context is required." });
            if (dto.Items == null || dto.Items.Count == 0) return BadRequest(new { message = "Choose at least one item to return." });
            if (dto.Items.Any(i => i.Quantity <= 0)) return BadRequest(new { message = "Quantity must be at least 1." });
            if (dto.Items.GroupBy(i => i.OrderItemId).Any(g => g.Count() > 1)) return BadRequest(new { message = "Each item can be listed only once." });
            var method = (dto.RefundMethod ?? "").Trim().ToUpperInvariant();
            var exchange = dto.ExchangeItems is { Count: > 0 };
            if (exchange) method = "EXCHANGE";
            if (method != "CASH" && method != "ADJUST_DUE" && method != "EXCHANGE")
                return BadRequest(new { message = "Refund method must be CASH or ADJUST_DUE (or give exchange items)." });
            if (exchange && dto.ExchangeItems!.Any(x => x.Quantity <= 0)) return BadRequest(new { message = "Exchange quantity must be at least 1." });

            var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == dto.OrderId);
            if (order == null) return NotFound(new { message = "Order not found." });
            if (order.Status != "COMPLETED") return BadRequest(new { message = "Only completed orders can have items returned." });

            await using var tx = await _context.Database.BeginTransactionAsync();
            // Serialise returns on one order so the "already returned" count can't be raced past.
            await _context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtext({0}))", $"return:{companyId.Value}:{order.Id}");

            var items = await _context.OrderItems.Where(i => i.OrderId == order.Id).ToListAsync();
            var returned = await ReturnedQuantities(order.Id);
            var factor = Factor(order);
            decimal refund = 0, restockedCost = 0;
            var lines = new List<(OrderItem Item, ReturnLineDto Line, decimal UnitRefund)>();
            foreach (var l in dto.Items)
            {
                var item = items.FirstOrDefault(i => i.Id == l.OrderItemId);
                if (item == null) return BadRequest(new { message = "One of the items is not part of this order." });
                var remaining = item.Quantity - returned.GetValueOrDefault(item.Id);
                if (l.Quantity > remaining)
                    return BadRequest(new { message = remaining <= 0 ? "That item has already been returned." : $"Only {remaining} of that item can still be returned." });
                var unit = Math.Round(item.Price * factor, 2);
                refund += unit * l.Quantity;
                lines.Add((item, l, unit));
            }
            refund = Math.Round(refund, 2);

            var phone = PhoneUtil.NormalizeBd(order.CustomerPhone);
            if (method == "ADJUST_DUE" && phone == null)
                return BadRequest(new { message = "This sale has no customer phone number, so it can't be taken off an account. Refund in cash." });
            if (method == "CASH" && string.Equals(order.PaymentMethod, "CREDIT", StringComparison.OrdinalIgnoreCase))
            {
                var outstanding = await _context.CreditLedger.AsNoTracking().Where(e => e.CompanyId == companyId.Value && e.OrderId == order.Id).SumAsync(e => (decimal?)e.Amount) ?? 0m;
                if (outstanding > 0) return BadRequest(new { message = "The customer still owes money on this sale. Take the refund off their account instead of paying cash." });
            }

            // Stock goes back for resellable items; the report's cost of goods drops by their wholesale value.
            var products = await _context.Products.IgnoreQueryFilters().Where(p => p.CompanyId == companyId.Value && lines.Select(x => x.Item.ProductId).Contains(p.Id)).ToDictionaryAsync(p => p.Id);
            var returnNumber = $"RET-{DateTime.UtcNow.Ticks}";
            foreach (var (item, line, _) in lines)
            {
                if (!line.Restock) continue;
                var cost = products.TryGetValue(item.ProductId, out var p) ? p.WholesalePrice : 0m;
                restockedCost += cost * line.Quantity;
                await _context.Database.SqlQueryRaw<int>("SELECT sp_inventory_move({0},{1},{2},{3},{4},{5},{6},{7})",
                    companyId.Value, item.ProductId, line.Quantity, "RETURN", Clip(dto.Reason, 200) ?? "Customer return", cost, returnNumber, _context.CurrentUserId).ToListAsync();
            }

            decimal cashRefunded = 0, customerPays = 0; int? newOrderId = null;
            if (method == "CASH") cashRefunded = refund;
            else if (method == "ADJUST_DUE")
            {
                await CreditLedger.LockAsync(_context, companyId.Value, phone!);
                _context.CreditLedger.Add(CreditLedger.Entry(companyId.Value, phone!, order.CustomerName, "RETURN", -refund, order.Id, null, $"Return {returnNumber}", _context.CurrentUserId));
            }
            else // EXCHANGE: the returned goods' value pays for the new goods; any difference is settled in cash
            {
                var sold = new List<(Product P, int Qty)>();
                decimal newSubtotal = 0;
                foreach (var x in dto.ExchangeItems!)
                {
                    // Fresh read (not the tracked copy): the returned goods were just put back on the shelf.
                    var product = await _context.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == x.ProductId);
                    if (product == null) return BadRequest(new { message = $"Product {x.ProductId} not found." });
                    if (product.StockQuantity < x.Quantity)
                        return BadRequest(new { message = $"Not enough stock of '{product.Name}'. Available: {product.StockQuantity}" });
                    newSubtotal += product.Price * x.Quantity;
                    sold.Add((product, x.Quantity));
                }
                var creditApplied = Math.Min(refund, newSubtotal);
                cashRefunded = refund - creditApplied;
                customerPays = newSubtotal - creditApplied;
                var number = $"POS-{DateTime.UtcNow.Ticks}";
                int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var cashier);
                newOrderId = (await _context.Database.SqlQueryRaw<int>(
                    "SELECT sp_checkout_order({0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15})",
                    companyId.Value, number, "POS", cashier, order.CustomerName ?? "Walk-in Customer", order.CustomerPhone ?? "",
                    "COMPLETED", newSubtotal, 0m, newSubtotal, "CASH", "PAID",
                    sold.Select(s => s.P.Id).ToArray(), sold.Select(s => s.Qty).ToArray(), sold.Select(s => s.P.Price).ToArray(), cashier).ToListAsync()).FirstOrDefault();
                await _context.Database.ExecuteSqlRawAsync("UPDATE orders SET discount_label = {0}, order_notes = {1} WHERE id = {2}",
                    "Exchange", $"Exchange for {order.OrderNumber} ({returnNumber}). Value of returned goods {refund:0.##}; customer pays {customerPays:0.##}.", newOrderId);
            }

            var ret = new OrderReturn
            {
                CompanyId = companyId.Value, OrderId = order.Id, ReturnNumber = returnNumber, Kind = exchange ? "EXCHANGE" : "RETURN",
                RefundAmount = refund, RefundMethod = method, CashRefunded = cashRefunded, RestockedCost = restockedCost,
                NewOrderId = newOrderId, Reason = Clip(dto.Reason, 300), RecordedBy = _context.CurrentUserId,
                Items = lines.Select(l => new OrderReturnItem { OrderItemId = l.Item.Id, ProductId = l.Item.ProductId, Quantity = l.Line.Quantity, UnitPrice = l.UnitRefund, Restocked = l.Line.Restock }).ToList()
            };
            _context.OrderReturns.Add(ret);
            await _context.SaveChangesAsync();
            await tx.CommitAsync();
            await Audit.LogAsync(_context, HttpContext, "order.return", $"{order.OrderNumber}: {returnNumber} refund {refund} via {method}");

            return Ok(new { ret.Id, returnNumber, kind = ret.Kind, refundAmount = refund, refundMethod = method, cashToGiveBack = cashRefunded, customerPays, newOrderId, restocked = lines.Where(l => l.Line.Restock).Sum(l => l.Line.Quantity) });
        }

        // An order-wide discount is shared out across its items, so a returned item gets back what was really paid for it.
        private static decimal Factor(Order o) => o.Subtotal > 0 ? Math.Max(0m, 1m - o.Discount / o.Subtotal) : 1m;

        private async Task<Dictionary<int, int>> ReturnedQuantities(int orderId) =>
            await (from ri in _context.OrderReturnItems
                   join r in _context.OrderReturns on ri.ReturnId equals r.Id
                   where r.OrderId == orderId
                   group ri by ri.OrderItemId into g
                   select new { g.Key, Qty = g.Sum(x => x.Quantity) }).ToDictionaryAsync(x => x.Key, x => x.Qty);

        private static string? Clip(string? s, int max)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim();
            return s.Length > max ? s[..max] : s;
        }
    }
}
