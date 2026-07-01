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
    public class PricingTagsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IFileTextLogger _fileLogger;

        public PricingTagsController(ApplicationDbContext context, IFileTextLogger fileLogger)
        {
            _context = context;
            _fileLogger = fileLogger;
        }

        [HttpGet]
        public async Task<IActionResult> GetPricingTags()
        {
            var companyId = _context.CompanyId ?? 0;
            var tags = await _context.Database
                .SqlQueryRaw<PricingTagListVm>("SELECT * FROM sp_get_pricing_tags({0})", companyId)
                .ToListAsync();
            return Ok(tags);
        }

        [HttpPost]
        public async Task<IActionResult> CreatePricingTag([FromBody] PricingTagUpsertDto dto)
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest("Company context is required.");
            if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest("Tag name is required.");

            var tagId = (await _context.Database.SqlQueryRaw<int>(
                "SELECT sp_create_pricing_tag({0},{1},{2},{3},{4},{5},{6},{7})",
                companyId.Value, dto.Name, dto.ProfitPercent, (object?)dto.DiscountPercent ?? DBNull.Value,
                (object?)dto.PromoStartDate ?? DBNull.Value, (object?)dto.PromoEndDate ?? DBNull.Value,
                dto.IsActive, _context.CurrentUserId
            ).ToListAsync()).FirstOrDefault();

            var created = await _context.PricingTags.FindAsync(tagId);
            return Ok(created);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdatePricingTag(int id, [FromBody] PricingTagUpsertDto dto)
        {
            if (!await _context.PricingTags.AnyAsync(t => t.Id == id)) return NotFound();
            if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest("Tag name is required.");

            await _context.Database.ExecuteSqlRawAsync(
                "CALL sp_update_pricing_tag({0},{1},{2},{3},{4},{5},{6},{7})",
                id, dto.Name, dto.ProfitPercent, (object?)dto.DiscountPercent ?? DBNull.Value,
                (object?)dto.PromoStartDate ?? DBNull.Value, (object?)dto.PromoEndDate ?? DBNull.Value,
                dto.IsActive, _context.CurrentUserId
            );

            var updated = await _context.PricingTags.FindAsync(id);
            return Ok(updated);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeletePricingTag(int id)
        {
            if (!await _context.PricingTags.AnyAsync(t => t.Id == id)) return NotFound();
            await _context.Database.ExecuteSqlRawAsync("CALL sp_delete_pricing_tag({0},{1})", id, _context.CurrentUserId);
            return NoContent();
        }
    }
}
