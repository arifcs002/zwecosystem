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
    public class OrdersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IFileTextLogger _fileLogger;
        private readonly ISmsSender _smsSender;
        private readonly IFraudAnalyzer _fraudAnalyzer;
        private readonly Microsoft.Extensions.Caching.Memory.IMemoryCache _cache;
        private readonly ICustomerAuth _customerAuth;
        private readonly IMetaCapiClient _capi;

        public OrdersController(ApplicationDbContext context, IFileTextLogger fileLogger, ISmsSender smsSender, IFraudAnalyzer fraudAnalyzer,
            Microsoft.Extensions.Caching.Memory.IMemoryCache cache, ICustomerAuth customerAuth, IMetaCapiClient capi)
        {
            _context = context;
            _fileLogger = fileLogger;
            _smsSender = smsSender;
            _fraudAnalyzer = fraudAnalyzer;
            _cache = cache;
            _customerAuth = customerAuth;
            _capi = capi;
        }

        // ── Storefront: customer-facing order tracking ─────────────────────────────
        // Anonymous. A caller must know BOTH the order number and the phone it was
        // placed with; any mismatch gets the same 404 so neither can be probed alone.
        // Only customer-safe fields are returned (no internal notes, no fraud data).
        [HttpGet("track")]
        [AllowAnonymous]
        public async Task<IActionResult> Track([FromQuery] string? orderNumber, [FromQuery] string? phone)
        {
            if (!_context.CompanyId.HasValue) return BadRequest(new { message = "Store context is required." });
            if (RateLimit.Exceeded(_cache, $"order-track:{RateLimit.ClientKey(HttpContext)}", 20, TimeSpan.FromMinutes(1)))
                return StatusCode(429, new { message = "Too many attempts. Please wait a minute." });

            var notFound = NotFound(new { message = "We couldn't find an order with those details." });
            var number = (orderNumber ?? "").Trim();
            var wanted = PhoneUtil.Last10Digits(phone);
            if (number.Length == 0 || number.Length > 64 || wanted.Length < 10) return notFound;

            var order = await _context.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.OrderNumber == number);
            if (order == null || PhoneUtil.Last10Digits(order.CustomerPhone) != wanted) return notFound;

            var vm = (await _context.Database
                .SqlQueryRaw<OrderListVm>("SELECT * FROM sp_get_order({0})", order.Id).ToListAsync()).FirstOrDefault();
            var items = ParseItems(vm?.ItemsJson);
            var timeline = await _context.OrderStatusHistory.AsNoTracking()
                .Where(h => h.OrderId == order.Id && h.CompanyId == order.CompanyId)
                .OrderBy(h => h.CreatedDate)
                .Select(h => new { h.Status, date = h.CreatedDate })
                .ToListAsync();

            return Ok(new
            {
                order.OrderNumber, order.Status, order.PaymentMethod, order.PaymentStatus,
                createdDate = order.CreatedDate,
                order.Subtotal, order.Discount, order.ShippingFee, order.Total,
                order.CouponCode, order.DiscountLabel, order.ShippingAddress, order.CourierName, order.TrackingNumber, order.CourierStatus,
                items, timeline
            });
        }

        // Only plain http(s) URLs and cookie-safe tokens are forwarded to Meta.
        private static string? SafeUrl(string? u) =>
            !string.IsNullOrWhiteSpace(u) && u.Length <= 500 && Uri.TryCreate(u, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) ? uri.ToString() : null;

        private static string? SafeToken(string? t) =>
            !string.IsNullOrWhiteSpace(t) && t.Length <= 200 && System.Text.RegularExpressions.Regex.IsMatch(t, @"^[A-Za-z0-9._\-]+$") ? t : null;

        private static System.Text.Json.JsonElement ParseItems(string? json)
        {
            try { return System.Text.Json.JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json).RootElement.Clone(); }
            catch (System.Text.Json.JsonException) { return System.Text.Json.JsonDocument.Parse("[]").RootElement.Clone(); }
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
var expiredUnits = await Batches.ExpiredQtyAsync(_context, companyId.Value, item.ProductId);                if (expiredUnits > 0 && product.StockQuantity - expiredUnits < item.Quantity)                    return BadRequest(new { message = $"'{product.Name}' is currently not available." });

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

            // Discount: a logged-in member of a customer group gets the group's percentage; a coupon
            // gives its own. They do NOT stack — the shopper gets whichever is larger. The coupon is
            // re-evaluated here from the DB (the preview endpoint is advisory only) and is only
            // redeemed when it is the discount actually applied, atomically so two concurrent
            // checkouts can't exceed its usage limit.
            var account = await _customerAuth.ResolveAsync(Request);
            decimal groupDiscount = 0;
            string? groupLabel = null;
            if (account?.GroupId != null)
            {
                var group = await _context.CustomerGroups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == account.GroupId);
                if (group != null && group.DiscountPercent > 0)
                {
                    groupDiscount = Math.Round(Math.Min(subtotal * group.DiscountPercent / 100m, subtotal), 2, MidpointRounding.AwayFromZero);
                    groupLabel = $"{group.Name} member {group.DiscountPercent:0.##}%";
                }
            }

            // Automatic offers compete with the group discount and coupon the same way: the shopper gets the largest one.
            var offerResult = OfferEngine.Compute(await OfferEngine.LoadActiveAsync(_context, companyId.Value),
                productIds.Select((pid, i) => new CartLine(pid, quantities[i], prices[i])));
            decimal discount = groupDiscount;
            string? discountLabel = groupLabel;
            if (offerResult.Discount > discount) { discount = offerResult.Discount; discountLabel = "Offer: " + string.Join(", ", offerResult.Labels); groupDiscount = discount; }
            Coupon? coupon = null;      // set only when the coupon is the discount applied (and was redeemed)
            if (!string.IsNullOrWhiteSpace(dto.CouponCode))
            {
                var code = dto.CouponCode.Trim().ToUpperInvariant();
                var found = await _context.Coupons.AsNoTracking().FirstOrDefaultAsync(c => c.Code == code);
                var eval = CouponRules.Evaluate(found, subtotal, DateTime.UtcNow);
                if (!eval.Ok) return BadRequest(new { message = eval.Message });

                if (eval.Discount >= groupDiscount)
                {
                    var redeemed = await _context.Database.ExecuteSqlRawAsync(
                        @"UPDATE coupons SET used_count = used_count + 1
                          WHERE id = {0} AND company_id = {1} AND is_active AND is_deleted = 0
                            AND (usage_limit IS NULL OR used_count < usage_limit)", found!.Id, companyId.Value);
                    if (redeemed == 0) return BadRequest(new { message = "This coupon has reached its usage limit." });
                    coupon = found;
                    discount = eval.Discount;
                    discountLabel = $"Coupon {found.Code}";
                }
            }

            var total = subtotal + shippingFee - discount;
            var orderNumber = $"WEB-{DateTime.UtcNow.Ticks}";
            var payment = string.IsNullOrWhiteSpace(dto.PaymentMethod) ? "COD" : dto.PaymentMethod;

            int orderId;
            try
            {
                orderId = (await _context.Database.SqlQueryRaw<int>(
                    "SELECT sp_create_online_order({0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14})",
                    companyId.Value, orderNumber,
                    dto.CustomerName, dto.CustomerPhone,
                    dto.ShippingAddress, dto.ShippingDistrict ?? "", dto.ShippingThana ?? "",
                    dto.OrderNotes ?? "",
                    subtotal, shippingFee, total, payment,
                    productIds.ToArray(), quantities.ToArray(), prices.ToArray()
                ).ToListAsync()).FirstOrDefault();
            }
            catch
            {
                // Order failed after the coupon was redeemed — hand the redemption back.
                if (coupon != null)
                    await _context.Database.ExecuteSqlRawAsync(
                        "UPDATE coupons SET used_count = GREATEST(used_count - 1, 0) WHERE id = {0} AND company_id = {1}",
                        coupon.Id, companyId.Value);
                throw;
            }

for (var bi = 0; bi < productIds.Count; bi++) await Batches.ConsumeAsync(_context, companyId.Value, productIds[bi], quantities[bi]);
            var couponCodeUsed = coupon?.Code;
            var accountId = account?.Id;
            if (discount > 0 || accountId != null)
            {
                await _context.Orders.Where(o => o.Id == orderId).ExecuteUpdateAsync(s => s
                    .SetProperty(o => o.Discount, discount)
                    .SetProperty(o => o.CouponCode, couponCodeUsed)
                    .SetProperty(o => o.DiscountLabel, discountLabel)
                    .SetProperty(o => o.CustomerAccountId, accountId));
            }
            // The shopper finished checkout — close any abandoned-cart follow-up for this phone.
            var normalizedPhone = PhoneUtil.NormalizeBd(dto.CustomerPhone);
            if (normalizedPhone != null)
            {
                await _context.AbandonedCarts
                    .Where(c => (c.Status == "OPEN" || c.Status == "CONTACTED")
                                && (c.CustomerPhone == normalizedPhone || (dto.SessionKey != null && c.SessionKey == dto.SessionKey)))
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(c => c.Status, "RECOVERED")
                        .SetProperty(c => c.RecoveredOrderId, orderId)
                        .SetProperty(c => c.UpdatedDate, DateTime.UtcNow));
            }

            // Meta Conversions API: a server-side Purchase that pairs with the browser pixel via the shared
            // event_id (the order number). Fire-and-forget so Meta being slow or down can never affect checkout.
            var pixelId = settings.GetValueOrDefault("meta_pixel_id");
            var capiToken = settings.GetValueOrDefault("secret_meta_capi_token");
            if (MetaCapiClient.IsValidPixelId(pixelId) && !string.IsNullOrWhiteSpace(capiToken))
            {
                var items = productIds.Select((pid, i) => (pid, quantities[i], prices[i])).ToList();
                var purchase = new CapiPurchase(orderNumber, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), SafeUrl(dto.SourceUrl),
                    dto.CustomerPhone, account?.Email, RateLimit.ClientKey(HttpContext), Request.Headers.UserAgent.ToString(),
                    SafeToken(dto.Fbp), SafeToken(dto.Fbc), account?.Id.ToString(), total, "BDT", items);
                var testCode = settings.GetValueOrDefault("meta_test_event_code");
                var capi = _capi; var log = _fileLogger; var pid2 = pixelId!; var tok = capiToken!.Trim();
                _ = Task.Run(async () =>
                {
                    var err = await capi.SendPurchaseAsync(pid2, tok, testCode, purchase);
                    if (err != null) log.LogError("CAPI", $"Meta Purchase for {orderNumber} failed: {err}");
                });
            }

            await RunFraudCheckAsync(orderId, companyId.Value, dto.CustomerPhone, total);

            return Ok(new { orderId, orderNumber, subtotal, shippingFee, discount, discountLabel, total });
        }

        // Screens the just-created order for fraud signals (velocity, unusual
        // amount, etc). Advisory only — never blocks checkout; a BLOCK/REVIEW
        // decision just surfaces the order via api/fraud/flagged for admin review.
        private async Task RunFraudCheckAsync(int orderId, int companyId, string? customerPhone, decimal total)
        {
            try
            {
                var ip = Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',').FirstOrDefault()?.Trim()
                         ?? HttpContext.Connection.RemoteIpAddress?.ToString();

                var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == orderId);
                if (order == null) return;

                order.CustomerIp = ip;
                await _context.SaveChangesAsync();

                var analysis = await _fraudAnalyzer.AnalyzeAsync(order, ip);

                _context.FraudChecks.Add(new FraudCheck
                {
                    CompanyId = companyId,
                    OrderId = orderId,
                    IpAddress = ip,
                    RiskScore = analysis.RiskScore,
                    Flags = string.Join(",", analysis.Flags),
                    Decision = analysis.Decision,
                    CheckedDate = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _fileLogger.LogError("FRAUD", $"Fraud check failed for order {orderId}", ex);
            }
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
            var detail = await BuildOrderDetailAsync(id);
            if (detail == null) return NotFound();
            return Ok(detail);
        }

        // Order detail for the admin UI. sp_get_order has no company filter, so the
        // tenant check MUST happen here first: _context.Orders carries the per-company
        // query filter, so another company's order id resolves to null (-> 404)
        // instead of leaking its customer data and line items.
        private async Task<object?> BuildOrderDetailAsync(int id)
        {
            var entity = await _context.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id);
            if (entity == null) return null;

            var vm = (await _context.Database
                .SqlQueryRaw<OrderListVm>("SELECT * FROM sp_get_order({0})", id)
                .ToListAsync()).FirstOrDefault();
            if (vm == null) return null;

            // The SP returns line items as a JSON string; the UI expects a real array.
            System.Text.Json.JsonElement items;
            try { items = System.Text.Json.JsonDocument.Parse(string.IsNullOrWhiteSpace(vm.ItemsJson) ? "[]" : vm.ItemsJson).RootElement.Clone(); }
            catch (System.Text.Json.JsonException) { items = System.Text.Json.JsonDocument.Parse("[]").RootElement.Clone(); }

            return new
            {
                vm.Id, vm.OrderNumber, vm.CustomerName, vm.CustomerPhone, vm.Total,
                vm.Status, vm.PaymentMethod, vm.PaymentStatus, vm.SaleType, vm.CreatedDate,
                entity.Subtotal, entity.Discount, entity.Tax, entity.ShippingFee,
                entity.ShippingAddress, entity.CourierName, entity.TrackingNumber,
                entity.CourierConsignmentId, entity.CourierStatus, entity.DiscountLabel,
                Items = items
            };
        }

        [HttpPut("{id}/status")]
        public async Task<IActionResult> UpdateOrderStatus(int id, [FromBody] StatusUpdateDto dto)
        {
            var orderEntity = await _context.Orders.FirstOrDefaultAsync(o => o.Id == id);
            if (orderEntity == null) return NotFound();

            if (string.Equals(dto.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
            {
                var (cancelled, error) = await CancelAsync(orderEntity);
                if (!cancelled) return Conflict(new { message = error });
            }
            else
            {
                await _context.Database.ExecuteSqlRawAsync(
                    "CALL sp_update_order_status({0},{1},{2})", id, dto.Status, dto.Notes ?? "");
                // Online orders earn loyalty points once they are completed (POS sales earn at the till).
                // The unique index on (order, EARN) makes a repeated COMPLETED update harmless.
                if (string.Equals(dto.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase) && orderEntity.SaleType != "POS")
                {
                    var lp = PhoneUtil.NormalizeBd(orderEntity.CustomerPhone);
                    var lc = await Loyalty.ConfigAsync(_context, orderEntity.CompanyId);
                    if (lp != null && lc.Enabled)
                    {
                        var pts = Loyalty.PointsFor(lc, Math.Max(orderEntity.Total - orderEntity.ShippingFee, 0));
                        if (pts > 0) await Loyalty.AddAsync(_context, orderEntity.CompanyId, lp, "EARN", pts, orderEntity.Id, $"Order {orderEntity.OrderNumber}", _context.CurrentUserId);
                    }
                }
            }

            // Record the change on the timeline.
            _context.OrderStatusHistory.Add(new OrderStatusHistory
            {
                OrderId     = id,
                CompanyId   = orderEntity.CompanyId,
                Status      = dto.Status,
                Note        = dto.Notes,
                ChangedBy   = _context.CurrentUserId,
                CreatedDate = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            // Best-effort SMS to the customer (only if this company configured a
            // gateway URL in Settings). Never let an SMS failure fail the request.
            await Audit.LogAsync(_context, HttpContext, "order.status", $"{orderEntity.OrderNumber} -> {dto.Status}" + (string.IsNullOrWhiteSpace(dto.Notes) ? "" : $" ({dto.Notes})"));
            await TryNotifyCustomerAsync(orderEntity, dto.Status);

            return Ok(await BuildOrderDetailAsync(id));
        }

        // ── GET /{id}/history — the order's status timeline ────────────────────
        [HttpGet("{id}/history")]
        public async Task<IActionResult> GetOrderHistory(int id)
        {
            if (!await _context.Orders.AnyAsync(o => o.Id == id)) return NotFound();
            var history = await _context.OrderStatusHistory
                .Where(h => h.OrderId == id)
                .OrderBy(h => h.CreatedDate)
                .Select(h => new { h.Status, h.Note, h.CreatedDate, h.ChangedBy })
                .ToListAsync();
            return Ok(history);
        }

        // ── PUT /{id}/courier — set courier name + tracking number ─────────────
        [HttpPut("{id}/courier")]
        public async Task<IActionResult> UpdateCourier(int id, [FromBody] CourierUpdateDto dto)
        {
            var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == id);
            if (order == null) return NotFound();

            order.CourierName    = string.IsNullOrWhiteSpace(dto.CourierName) ? null : dto.CourierName.Trim();
            order.TrackingNumber = string.IsNullOrWhiteSpace(dto.TrackingNumber) ? null : dto.TrackingNumber.Trim();
            await _context.SaveChangesAsync();

            await Audit.LogAsync(_context, HttpContext, "order.courier", $"{order.OrderNumber}: {order.CourierName} {order.TrackingNumber}");
            return Ok(new { order.Id, order.CourierName, order.TrackingNumber });
        }

        // Sends a status-update SMS if the company set an sms_api_url template in
        // its settings. Silently skips otherwise. Message text is templated per
        // status; the storefront name is included when available.
        private async Task TryNotifyCustomerAsync(Order order, string status)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(order.CustomerPhone)) return;

                var settings = await _context.CompanySettings
                    .Where(s => s.CompanyId == order.CompanyId)
                    .ToDictionaryAsync(s => s.Key, s => s.Value);

                var apiUrl = settings.GetValueOrDefault("sms_api_url");
                if (string.IsNullOrWhiteSpace(apiUrl)) return;

                var shopName = settings.GetValueOrDefault("shop_name")
                    ?? settings.GetValueOrDefault("store_name") ?? "our store";

                var msg = status.ToUpperInvariant() switch
                {
                    "PROCESSING" => $"Your order {order.OrderNumber} at {shopName} is being processed.",
                    "PACKED"     => $"Your order {order.OrderNumber} at {shopName} has been packed.",
                    "SHIPPED"    => $"Good news! Your order {order.OrderNumber} from {shopName} has been shipped"
                                    + (string.IsNullOrWhiteSpace(order.TrackingNumber) ? "." : $" (tracking: {order.TrackingNumber}).") ,
                    "COMPLETED"  => $"Your order {order.OrderNumber} from {shopName} has been delivered. Thank you!",
                    "CANCELLED"  => $"Your order {order.OrderNumber} at {shopName} has been cancelled.",
                    _             => $"Your order {order.OrderNumber} status: {status}."
                };

                // The shop's own wording wins: sms_template_<status> with {order} {shop} {name} {tracking} {total}.
                var template = settings.GetValueOrDefault("sms_template_" + status.ToLowerInvariant());
                if (!string.IsNullOrWhiteSpace(template))
                {
                    msg = template
                        .Replace("{order}", order.OrderNumber)
                        .Replace("{shop}", shopName)
                        .Replace("{name}", order.CustomerName ?? "")
                        .Replace("{tracking}", order.TrackingNumber ?? "")
                        .Replace("{total}", order.Total.ToString("0.##"));
                }

                await _smsSender.SendAsync(apiUrl, order.CustomerPhone, msg);
            }
            catch (Exception ex)
            {
                _fileLogger.LogError("SMS", $"Notify failed for order {order.Id}", ex);
            }
        }

        [HttpPost("{id}/cancel")]
        public async Task<IActionResult> CancelOrder(int id)
        {
            var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == id);
            if (order == null) return NotFound();
            var (ok, error) = await CancelAsync(order);
            if (!ok) return Conflict(new { message = error });
            await Audit.LogAsync(_context, HttpContext, "order.cancel", $"order id {id}");
            return Ok(new { message = "Order cancelled." });
        }

        // Cancelling puts the stock back and writes off any unpaid credit — and must only ever happen ONCE,
        // otherwise stock is added twice. Orders that already had items returned can't be cancelled (the
        // returned items are already back in stock): the rest of such an order is handled with another return.
        private async Task<(bool Ok, string? Error)> CancelAsync(Order order)
        {
            if (order.Status == "CANCELLED") return (false, "This order is already cancelled.");
            if (await _context.OrderReturns.AnyAsync(r => r.OrderId == order.Id))
                return (false, "Some items of this order were already returned, so it can't be cancelled. Return the remaining items instead.");

            await using var tx = await _context.Database.BeginTransactionAsync();
            await _context.Database.ExecuteSqlRawAsync("CALL sp_cancel_order({0})", order.Id);
            if (string.Equals(order.PaymentMethod, "CREDIT", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(order.CustomerPhone))
                await CreditLedger.ReverseOrderAsync(_context, order.CompanyId, order.Id, order.CustomerPhone, order.CustomerName, _context.CurrentUserId, $"Order {order.OrderNumber} cancelled");
            await Loyalty.ReverseOnCancelAsync(_context, order.CompanyId, order.Id, _context.CurrentUserId);
            await tx.CommitAsync();
            order.Status = "CANCELLED";
            return (true, null);
        }
    }
}
