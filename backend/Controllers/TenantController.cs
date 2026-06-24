using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ecommerce.Api.Domain;
using Ecommerce.Api.Infrastructure;
using System.Text.Json;

namespace Ecommerce.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TenantController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public TenantController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet("{subdomain}")]
        public async Task<IActionResult> GetTenantConfig(string subdomain)
        {
            var company = await _context.Companies
                .FirstOrDefaultAsync(c => c.Subdomain == subdomain);

            if (company == null) return NotFound(new { message = "Company not found" });

            var settings = await _context.CompanySettings
                .Where(s => s.CompanyId == company.Id)
                .ToDictionaryAsync(s => s.Key, s => s.Value);

            var config = new
            {
                CompanyId = company.Id,
                Name = company.Name,
                LogoUrl = company.LogoUrl,
                BannerUrl = company.BannerUrl,
                ThemeColor = settings.GetValueOrDefault("THEME_COLOR", "#4A90E2"),
                Facebook = settings.GetValueOrDefault("SOCIAL_FACEBOOK", ""),
                DashboardCategories = settings.GetValueOrDefault("DASHBOARD_CATEGORIES", "[]")
            };

            return Ok(config);
        }
        
        [HttpPost("{companyId}/settings")]
        public async Task<IActionResult> SaveSettings(Guid companyId, [FromBody] System.Collections.Generic.Dictionary<string, string> newSettings)
        {
            var existingSettings = await _context.CompanySettings.Where(s => s.CompanyId == companyId).ToListAsync();
            
            foreach (var kvp in newSettings)
            {
                var setting = existingSettings.FirstOrDefault(s => s.Key == kvp.Key);
                if (setting != null)
                {
                    setting.Value = kvp.Value;
                }
                else
                {
                    _context.CompanySettings.Add(new CompanySetting
                    {
                        CompanyId = companyId,
                        Key = kvp.Key,
                        Value = kvp.Value,
                        GroupName = "GENERAL"
                    });
                }
            }
            
            await _context.SaveChangesAsync();
            return Ok(new { message = "Settings saved successfully" });
        }
    }
}
