using System.Text.Json;
using System.Text.RegularExpressions;
using Ecommerce.Api.Domain;
using Ecommerce.Api.Infrastructure;
using Ecommerce.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Ecommerce.Api.Controllers
{
    [ApiController]
    [Route("api/abandoned-carts")]
    [Authorize]
    public class AbandonedCartsController : ControllerBase
    {
        private static readonly string[] Statuses = { "OPEN", "CONTACTED", "RECOVERED", "DISMISSED" };
        private static readonly Regex SessionPattern = new(@"^[A-Za-z0-9\-]{8,64}$", RegexOptions.Compiled);
        private readonly ApplicationDbContext _context;
        private readonly IMemoryCache _cache;

        public AbandonedCartsController(ApplicationDbContext context, IMemoryCache cache)
        {
            _context = context;
            _cache = cache;
        }

        // ── Storefront: remember a cart once the visitor has typed a valid phone ──
        // Anonymous (tenant from X-Tenant-ID). The cart snapshot is rebuilt from DB
        // product rows, so only ids/quantities from the client are used.
        [HttpPost("capture")]
        [AllowAnonymous]
        public async Task<IActionResult> Capture([FromBody] CartCaptureDto dto)
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest(new { message = "Store context is required." });
            if (RateLimit.Exceeded(_cache, $"cart-capture:{RateLimit.ClientKey(HttpContext)}", 60, TimeSpan.FromMinutes(1)))
                return StatusCode(429, new { message = "Too many requests." });

            var phone = PhoneUtil.NormalizeBd(dto.CustomerPhone);
            if (phone == null || dto.SessionKey == null || !SessionPattern.IsMatch(dto.SessionKey))
                return BadRequest(new { message = "A valid mobile number is required." });
            if (dto.Items == null || dto.Items.Count == 0 || dto.Items.Count > 50)
                return BadRequest(new { message = "Cart is empty." });

            var lines = dto.Items.Where(i => i.Quantity > 0 && i.Quantity <= 1000).ToList();
            var ids = lines.Select(i => i.ProductId).Distinct().ToList();
            var products = await _context.Products.AsNoTracking()
                .Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
            var snapshot = lines.Where(l => products.ContainsKey(l.ProductId))
                .Select(l => new
                {
                    productId = l.ProductId,
                    name = products[l.ProductId].Name,
                    quantity = l.Quantity,
                    price = products[l.ProductId].Price
                }).ToList();
            if (snapshot.Count == 0) return BadRequest(new { message = "Cart is empty." });

            var existing = await _context.AbandonedCarts
                .FirstOrDefaultAsync(c => c.SessionKey == dto.SessionKey);
            // A recovered or dismissed cart is a closed case — don't reopen it for the same session.
            if (existing != null && (existing.Status == "RECOVERED" || existing.Status == "DISMISSED"))
                return Ok(new { captured = false });

            var cart = existing ?? new AbandonedCart { CompanyId = companyId.Value, SessionKey = dto.SessionKey };
            cart.CustomerPhone = phone;
            cart.CustomerName = Clip(dto.CustomerName, 120);
            cart.ShippingAddress = Clip(dto.ShippingAddress, 400);
            cart.CartJson = JsonSerializer.Serialize(snapshot);
            cart.ItemCount = snapshot.Sum(s => s.quantity);
            cart.Subtotal = snapshot.Sum(s => s.price * s.quantity);
            cart.LastActivity = DateTime.UtcNow;
            cart.UpdatedDate = DateTime.UtcNow;
            if (existing == null) _context.AbandonedCarts.Add(cart);

            try { await _context.SaveChangesAsync(); }
            catch (DbUpdateException) { return Ok(new { captured = false }); } // lost a race on the unique session key
            return Ok(new { captured = true });
        }

        // ── Admin ────────────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] string? status)
        {
            var q = _context.AbandonedCarts.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(status) && Statuses.Contains(status.ToUpperInvariant()))
                q = q.Where(c => c.Status == status.ToUpperInvariant());

            var rows = await q.OrderByDescending(c => c.LastActivity).Take(500)
                .Select(c => new
                {
                    c.Id, c.CustomerName, c.CustomerPhone, c.ShippingAddress, c.ItemCount, c.Subtotal,
                    c.Status, c.LastActivity, c.CartJson
                }).ToListAsync();

            return Ok(rows.Select(r => new
            {
                r.Id, r.CustomerName, r.CustomerPhone, r.ShippingAddress, r.ItemCount, r.Subtotal,
                r.Status, r.LastActivity,
                items = JsonSerializer.Deserialize<JsonElement>(string.IsNullOrWhiteSpace(r.CartJson) ? "[]" : r.CartJson)
            }));
        }

        [HttpPut("{id}/status")]
        public async Task<IActionResult> SetStatus(int id, [FromBody] AbandonedCartStatusDto dto)
        {
            var status = (dto.Status ?? "").ToUpperInvariant();
            if (!Statuses.Contains(status)) return BadRequest(new { message = "Invalid status." });
            var cart = await _context.AbandonedCarts.FirstOrDefaultAsync(c => c.Id == id);
            if (cart == null) return NotFound();
            cart.Status = status;
            cart.UpdatedDate = DateTime.UtcNow;
            cart.UpdatedBy = _context.CurrentUserId;
            await _context.SaveChangesAsync();
            return Ok(new { cart.Id, cart.Status });
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var cart = await _context.AbandonedCarts.FirstOrDefaultAsync(c => c.Id == id);
            if (cart == null) return NotFound();
            cart.IsDeleted = 1;
            cart.DeletedDate = DateTime.UtcNow;
            cart.DeletedBy = _context.CurrentUserId;
            await _context.SaveChangesAsync();
            return Ok(new { message = "Removed." });
        }

        private static string? Clip(string? s, int max)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim();
            return s.Length > max ? s[..max] : s;
        }
    }
}
