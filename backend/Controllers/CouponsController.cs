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
    [Route("api/[controller]")]
    [Authorize]
    public class CouponsController : ControllerBase
    {
        private static readonly Regex CodePattern = new(@"^[A-Z0-9_-]{3,32}$", RegexOptions.Compiled);
        private readonly ApplicationDbContext _context;
        private readonly IMemoryCache _cache;

        public CouponsController(ApplicationDbContext context, IMemoryCache cache)
        {
            _context = context;
            _cache = cache;
        }

        // ── Storefront: preview a coupon against the cart ───────────────────────
        // Anonymous; tenant comes from X-Tenant-ID. The subtotal is rebuilt from the
        // DB prices, so a tampered client can't claim a bigger cart to unlock a discount.
        [HttpPost("validate")]
        [AllowAnonymous]
        public async Task<IActionResult> Validate([FromBody] CouponValidateDto dto)
        {
            if (!_context.CompanyId.HasValue) return BadRequest(new { message = "Store context is required." });
            if (RateLimit.Exceeded(_cache, $"coupon-validate:{RateLimit.ClientKey(HttpContext)}", 30, TimeSpan.FromMinutes(1)))
                return StatusCode(429, new { message = "Too many attempts. Please wait a minute." });
            if (string.IsNullOrWhiteSpace(dto.Code) || dto.Items == null || dto.Items.Count == 0)
                return Ok(new { valid = false, discount = 0m, message = "Enter a coupon code." });

            var code = dto.Code.Trim().ToUpperInvariant();
            var subtotal = await CartSubtotalAsync(dto.Items);
            var coupon = await _context.Coupons.AsNoTracking().FirstOrDefaultAsync(c => c.Code == code);
            var eval = CouponRules.Evaluate(coupon, subtotal, DateTime.UtcNow);
            return Ok(new { valid = eval.Ok, discount = eval.Discount, message = eval.Message, code });
        }

        // ── Admin CRUD (tenant-scoped by the global query filter) ───────────────
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var list = await _context.Coupons.AsNoTracking()
                .OrderByDescending(c => c.CreatedDate)
                .Select(c => new
                {
                    c.Id, c.Code, c.Description, c.DiscountType, c.DiscountValue, c.MinOrderAmount,
                    c.MaxDiscountAmount, c.UsageLimit, c.UsedCount, c.StartsAt, c.ExpiresAt, c.IsActive, c.CreatedDate
                })
                .ToListAsync();
            return Ok(list);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CouponUpsertDto dto)
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest(new { message = "Company context is required." });
            var error = CheckUpsert(dto, out var code);
            if (error != null) return BadRequest(new { message = error });
            if (await _context.Coupons.AnyAsync(c => c.Code == code))
                return BadRequest(new { message = $"Coupon code '{code}' already exists." });

            var coupon = new Coupon { CompanyId = companyId.Value, CreatedBy = _context.CurrentUserId };
            Apply(coupon, dto, code);
            _context.Coupons.Add(coupon);
            await _context.SaveChangesAsync();
            return Ok(new { coupon.Id, coupon.Code });
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] CouponUpsertDto dto)
        {
            var coupon = await _context.Coupons.FirstOrDefaultAsync(c => c.Id == id);
            if (coupon == null) return NotFound();
            var error = CheckUpsert(dto, out var code);
            if (error != null) return BadRequest(new { message = error });
            if (await _context.Coupons.AnyAsync(c => c.Code == code && c.Id != id))
                return BadRequest(new { message = $"Coupon code '{code}' already exists." });

            Apply(coupon, dto, code);
            coupon.UpdatedBy = _context.CurrentUserId;
            coupon.UpdatedDate = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return Ok(new { coupon.Id, coupon.Code });
        }

        [HttpPatch("{id}/toggle")]
        public async Task<IActionResult> Toggle(int id)
        {
            var coupon = await _context.Coupons.FirstOrDefaultAsync(c => c.Id == id);
            if (coupon == null) return NotFound();
            coupon.IsActive = !coupon.IsActive;
            coupon.UpdatedDate = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return Ok(new { coupon.Id, coupon.IsActive });
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var coupon = await _context.Coupons.FirstOrDefaultAsync(c => c.Id == id);
            if (coupon == null) return NotFound();
            coupon.IsDeleted = 1;
            coupon.DeletedDate = DateTime.UtcNow;
            coupon.DeletedBy = _context.CurrentUserId;
            await _context.SaveChangesAsync();
            return Ok(new { message = "Coupon deleted." });
        }

        private async Task<decimal> CartSubtotalAsync(List<OrderItemDto> items)
        {
            var ids = items.Where(i => i.Quantity > 0).Select(i => i.ProductId).Distinct().ToList();
            var prices = await _context.Products.AsNoTracking()
                .Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Price);
            return items.Where(i => i.Quantity > 0 && prices.ContainsKey(i.ProductId))
                        .Sum(i => prices[i.ProductId] * i.Quantity);
        }

        private static string? CheckUpsert(CouponUpsertDto dto, out string code)
        {
            code = (dto.Code ?? "").Trim().ToUpperInvariant();
            if (!CodePattern.IsMatch(code)) return "Code must be 3-32 characters: letters, numbers, - or _.";
            if (dto.DiscountType != "PERCENT" && dto.DiscountType != "FIXED") return "Discount type must be PERCENT or FIXED.";
            if (dto.DiscountValue <= 0) return "Discount value must be greater than 0.";
            if (dto.DiscountType == "PERCENT" && dto.DiscountValue > 100) return "A percentage discount cannot exceed 100.";
            if (dto.MinOrderAmount < 0) return "Minimum order amount cannot be negative.";
            if (dto.MaxDiscountAmount.HasValue && dto.MaxDiscountAmount.Value <= 0) return "Maximum discount must be greater than 0.";
            if (dto.UsageLimit.HasValue && dto.UsageLimit.Value < 1) return "Usage limit must be at least 1.";
            if (dto.StartsAt.HasValue && dto.ExpiresAt.HasValue && dto.ExpiresAt.Value <= dto.StartsAt.Value)
                return "Expiry must be after the start date.";
            return null;
        }

        private static void Apply(Coupon c, CouponUpsertDto dto, string code)
        {
            c.Code = code;
            c.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
            c.DiscountType = dto.DiscountType;
            c.DiscountValue = dto.DiscountValue;
            c.MinOrderAmount = dto.MinOrderAmount;
            c.MaxDiscountAmount = dto.DiscountType == "PERCENT" ? dto.MaxDiscountAmount : null;
            c.UsageLimit = dto.UsageLimit;
            c.StartsAt = dto.StartsAt?.ToUniversalTime();
            c.ExpiresAt = dto.ExpiresAt?.ToUniversalTime();
            c.IsActive = dto.IsActive;
        }
    }
}
