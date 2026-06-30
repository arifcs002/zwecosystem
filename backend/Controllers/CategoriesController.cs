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
    public class CategoriesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IFileTextLogger _fileLogger;

        public CategoriesController(ApplicationDbContext context, IFileTextLogger fileLogger)
        {
            _context = context;
            _fileLogger = fileLogger;
        }

        [HttpGet]
        public async Task<IActionResult> GetCategories()
        {
            var companyId = _context.CompanyId ?? 0;
            var categories = await _context.Database
                .SqlQueryRaw<CategoryListVm>("SELECT * FROM sp_get_categories({0})", companyId)
                .ToListAsync();
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
            return Ok(updated);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteCategory(int id)
        {
            if (!await _context.Categories.AnyAsync(c => c.Id == id)) return NotFound();
            await _context.Database.ExecuteSqlRawAsync("CALL sp_delete_category({0},{1})", id, _context.CurrentUserId);
            return NoContent();
        }
    }
}
