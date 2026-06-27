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
    public class SettingsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IFileTextLogger _fileLogger;

        public SettingsController(ApplicationDbContext context, IFileTextLogger fileLogger)
        {
            _context = context;
            _fileLogger = fileLogger;
        }

        [HttpGet]
        public async Task<IActionResult> GetSettings()
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest("Company context is required.");

            var settings = await _context.Database
                .SqlQueryRaw<SettingVm>("SELECT * FROM sp_get_settings({0})", companyId.Value)
                .ToListAsync();
            return Ok(settings);
        }

        [HttpPut]
        public async Task<IActionResult> UpdateSettings([FromBody] List<CompanySetting> settingsList)
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest("Company context is required.");

            foreach (var s in settingsList)
            {
                await _context.Database.ExecuteSqlRawAsync(
                    "CALL sp_upsert_setting({0},{1},{2},{3})",
                    companyId.Value, s.Key, s.Value, s.GroupName);
            }

            return Ok(new { message = "Settings updated successfully" });
        }

        [HttpPut("company-profile")]
        public async Task<IActionResult> UpdateCompanyProfile([FromBody] CompanyUpdateDto dto)
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest("Company context is required.");

            await _context.Database.ExecuteSqlRawAsync(
                "CALL sp_update_company_profile({0},{1},{2},{3},{4},{5},{6},{7})",
                companyId.Value, dto.Name, dto.LogoUrl ?? "",
                dto.BannerUrl ?? "", dto.ContactEmail ?? "",
                dto.ContactPhone ?? "", dto.Address ?? "", dto.DeliveryCharge);

            var company = await _context.Companies.FindAsync(companyId.Value);
            return Ok(company);
        }
    }
}
