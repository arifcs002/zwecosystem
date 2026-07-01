using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ecommerce.Api.Domain;
using Ecommerce.Api.Infrastructure;
using Ecommerce.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace Ecommerce.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class CategoriesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IFileTextLogger _fileLogger;
        private readonly IDistributedCache _cache;

        public CategoriesController(ApplicationDbContext context, IFileTextLogger fileLogger, IDistributedCache cache)
        {
            _context = context;
            _fileLogger = fileLogger;
            _cache = cache;
        }

        private string CategoriesCacheKey(int companyId) => $"categories:{companyId}";

        // Category list has no sensitive fields and is needed by the anonymous
        // storefront (category tree, sidebar) as well as the admin UI.
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetCategories()
        {
            var companyId = _context.CompanyId ?? 0;
            var cacheKey = CategoriesCacheKey(companyId);

            var cached = await _cache.GetStringAsync(cacheKey);
            if (cached != null)
                return Ok(JsonSerializer.Deserialize<List<CategoryListVm>>(cached));

            var categories = await _context.Database
                .SqlQueryRaw<CategoryListVm>("SELECT * FROM sp_get_categories({0})", companyId)
                .ToListAsync();

            // Categories change rarely (admin action), so cache for 5 minutes and
            // invalidate explicitly on create/update/delete below.
            await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(categories),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) });

            return Ok(categories);
        }

        [HttpPost]
        public async Task<IActionResult> CreateCategory([FromBody] CategoryUpsertDto dto)
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest("Company context is required.");
            if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest("Category name is required.");

            var slug = dto.Name.ToLower().Replace(" ", "-").Replace("/", "-");

            var categoryId = (await _context.Database.SqlQueryRaw<int>(
                "SELECT sp_create_category({0},{1},{2},{3},{4},{5},{6})",
                companyId.Value, dto.Name, slug,
                dto.Description ?? "", dto.Sizes ?? "", _context.CurrentUserId, (object?)dto.ParentId ?? DBNull.Value
            ).ToListAsync()).FirstOrDefault();

            var created = await _context.Categories.FindAsync(categoryId);
            await _cache.RemoveAsync(CategoriesCacheKey(companyId.Value));
            return Ok(created);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateCategory(int id, [FromBody] CategoryUpsertDto dto)
        {
            if (!await _context.Categories.AnyAsync(c => c.Id == id && c.IsDeleted == 0)) return NotFound();
            if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest("Category name is required.");

            var slug = dto.Name.ToLower().Replace(" ", "-").Replace("/", "-");
            await _context.Database.ExecuteSqlRawAsync(
                "CALL sp_update_category({0},{1},{2},{3},{4},{5},{6})",
                id, dto.Name, slug,
                dto.Description ?? "", dto.Sizes ?? "", _context.CurrentUserId, (object?)dto.ParentId ?? DBNull.Value
            );
            var updated = await _context.Categories.FindAsync(id);
            if (_context.CompanyId.HasValue) await _cache.RemoveAsync(CategoriesCacheKey(_context.CompanyId.Value));
            return Ok(updated);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteCategory(int id)
        {
            if (!await _context.Categories.AnyAsync(c => c.Id == id)) return NotFound();
            await _context.Database.ExecuteSqlRawAsync("CALL sp_delete_category({0},{1})", id, _context.CurrentUserId);
            if (_context.CompanyId.HasValue) await _cache.RemoveAsync(CategoriesCacheKey(_context.CompanyId.Value));
            return NoContent();
        }
    }
}
