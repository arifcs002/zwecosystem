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
    public class ReviewsController : ControllerBase
    {
        private static readonly string[] Statuses = { "PENDING", "APPROVED", "REJECTED" };
        private readonly ApplicationDbContext _context;
        private readonly IMemoryCache _cache;

        public ReviewsController(ApplicationDbContext context, IMemoryCache cache)
        {
            _context = context;
            _cache = cache;
        }

        // ── Storefront (anonymous; tenant from X-Tenant-ID) ─────────────────────

        // { "<productKey>": { average, count } } for every product that has approved reviews —
        // one small call lets list pages show stars on every card.
        [HttpGet("public/summary")]
        [AllowAnonymous]
        public async Task<IActionResult> Summary()
        {
            if (!_context.CompanyId.HasValue) return Ok(new Dictionary<string, object>());
            var rows = await _context.Reviews.AsNoTracking()
                .Where(r => r.Status == "APPROVED")
                .GroupBy(r => r.ProductKey)
                .Select(g => new { key = g.Key, average = g.Average(x => (double)x.Rating), count = g.Count() })
                .ToListAsync();
            return Ok(rows.ToDictionary(r => r.key, r => (object)new { average = Math.Round(r.average, 1), r.count }));
        }

        // Approved reviews for the product (all sizes) the id belongs to.
        [HttpGet("public/product/{productId}")]
        [AllowAnonymous]
        public async Task<IActionResult> ForProduct(int productId)
        {
            if (!_context.CompanyId.HasValue) return Ok(new { average = 0.0, count = 0, reviews = Array.Empty<object>() });
            var product = await _context.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId);
            if (product == null) return NotFound();

            var key = ProductKey.For(product);
            var rows = await _context.Reviews.AsNoTracking()
                .Where(r => r.ProductKey == key && r.Status == "APPROVED")
                .OrderByDescending(r => r.CreatedDate).Take(100)
                .Select(r => new { r.Id, r.ReviewerName, r.Rating, r.Comment, r.IsVerified, date = r.CreatedDate })
                .ToListAsync();
            return Ok(new
            {
                average = rows.Count == 0 ? 0.0 : Math.Round(rows.Average(r => (double)r.Rating), 1),
                count = rows.Count,
                reviews = rows
            });
        }

        // Verified purchasers (order number + phone match a real, non-cancelled order that contains
        // this product) are approved immediately; everyone else waits for the owner to approve.
        [HttpPost("public")]
        [AllowAnonymous]
        public async Task<IActionResult> Submit([FromBody] ReviewSubmitDto dto)
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest(new { message = "Store context is required." });
            if (RateLimit.Exceeded(_cache, $"review-submit:{RateLimit.ClientKey(HttpContext)}", 10, TimeSpan.FromMinutes(10)))
                return StatusCode(429, new { message = "Too many reviews. Please try again later." });

            var name = (dto.ReviewerName ?? "").Trim();
            var comment = (dto.Comment ?? "").Trim();
            if (name.Length < 2 || name.Length > 60) return BadRequest(new { message = "Please enter your name (2-60 characters)." });
            if (dto.Rating < 1 || dto.Rating > 5) return BadRequest(new { message = "Choose a rating from 1 to 5." });
            if (comment.Length > 1000) return BadRequest(new { message = "Review is too long (max 1000 characters)." });

            var product = await _context.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == dto.ProductId);
            if (product == null) return NotFound(new { message = "Product not found." });
            var key = ProductKey.For(product);

            var verified = false;
            if (!string.IsNullOrWhiteSpace(dto.OrderNumber) && !string.IsNullOrWhiteSpace(dto.Phone))
            {
                var order = await _context.Orders.AsNoTracking()
                    .FirstOrDefaultAsync(o => o.OrderNumber == dto.OrderNumber.Trim() && o.Status != "CANCELLED");
                if (order != null && PhoneUtil.Last10Digits(order.CustomerPhone) == PhoneUtil.Last10Digits(dto.Phone))
                {
                    var boughtProductIds = await _context.OrderItems.AsNoTracking()
                        .Where(i => i.OrderId == order.Id).Select(i => i.ProductId).ToListAsync();
                    var bought = await _context.Products.AsNoTracking()
                        .Where(p => boughtProductIds.Contains(p.Id)).ToListAsync();
                    verified = bought.Any(p => ProductKey.For(p) == key);
                }
            }

            var review = new Review
            {
                CompanyId = companyId.Value, ProductId = product.Id, ProductKey = key,
                ReviewerName = name, Rating = dto.Rating, Comment = comment.Length == 0 ? null : comment,
                IsVerified = verified, Status = verified ? "APPROVED" : "PENDING"
            };
            _context.Reviews.Add(review);
            await _context.SaveChangesAsync();
            return Ok(new { status = review.Status, verified,
                message = verified ? "Thanks! Your review is live." : "Thanks! Your review will appear after the shop approves it." });
        }

        // ── Admin moderation ─────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] string? status)
        {
            var q = _context.Reviews.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(status) && Statuses.Contains(status.ToUpperInvariant()))
                q = q.Where(r => r.Status == status.ToUpperInvariant());

            var rows = await q.OrderByDescending(r => r.CreatedDate).Take(500).ToListAsync();
            var ids = rows.Select(r => r.ProductId).Distinct().ToList();
            var names = await _context.Products.IgnoreQueryFilters().AsNoTracking()
                .Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => ProductKey.BaseName(p.Name));
            return Ok(rows.Select(r => new
            {
                r.Id, r.ProductId, productName = names.GetValueOrDefault(r.ProductId, "(deleted product)"),
                r.ReviewerName, r.Rating, r.Comment, r.Status, r.IsVerified, date = r.CreatedDate
            }));
        }

        [HttpPut("{id}/status")]
        public async Task<IActionResult> SetStatus(int id, [FromBody] ReviewStatusDto dto)
        {
            var status = (dto.Status ?? "").ToUpperInvariant();
            if (!Statuses.Contains(status)) return BadRequest(new { message = "Invalid status." });
            var review = await _context.Reviews.FirstOrDefaultAsync(r => r.Id == id);
            if (review == null) return NotFound();
            review.Status = status;
            review.UpdatedDate = DateTime.UtcNow;
            review.UpdatedBy = _context.CurrentUserId;
            await _context.SaveChangesAsync();
            await Audit.LogAsync(_context, HttpContext, "review." + status.ToLowerInvariant(), $"{review.ReviewerName} ({review.Rating} stars)");
            return Ok(new { review.Id, review.Status });
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var review = await _context.Reviews.FirstOrDefaultAsync(r => r.Id == id);
            if (review == null) return NotFound();
            review.IsDeleted = 1;
            review.DeletedDate = DateTime.UtcNow;
            review.DeletedBy = _context.CurrentUserId;
            await _context.SaveChangesAsync();
            await Audit.LogAsync(_context, HttpContext, "review.delete", review.ReviewerName);
            return Ok(new { message = "Review deleted." });
        }
    }
}
