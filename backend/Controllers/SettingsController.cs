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

        // Only these keys (plus the per-category "category_order_{id}" keys below)
        // are safe to hand to an anonymous storefront visitor — branding/display
        // config, never anything that could be a secret. Keep in sync with what
        // dashboard-config writes for public consumption.
        private const string CategoryOrderKeyPrefix = "category_order_";
        // Public so the storefront aggregate endpoint can apply the same filter.
        public static readonly HashSet<string> PublicSettingKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            // Branding / general
            "store_name", "store_phone", "primary_color", "logo_url",
            "shop_currency", "visible_dashboard_categories",

            // Footer + social
            "footer_about_text", "facebook_link", "instagram_link",
            "twitter_link", "youtube_link", "whatsapp_link", "tiktok_link", "linkedin_link",

            // Storefront builder (homepage block layout) + header/nav
            "storefront_layout", "nav_categories",
            "announcement_text", "announcement_link", "announcement_enabled",

            // Product display rules
            "new_badge_days", "low_stock_threshold",
            "enable_whatsapp_order", "whatsapp_number",
            "enable_call_order", "call_number",

            // Checkout / payment (public needs these to render the checkout UI)
            "delivery_charge", "free_delivery_above",
            "payment_cod", "payment_bkash", "payment_online", "bkash_number",

            // Static pages / policies + contact
            "page_about", "page_privacy", "page_refund", "page_terms",
            "contact_address", "contact_phone", "contact_email"
        };

        public static bool IsPublicSettingKey(string key) =>
            PublicSettingKeys.Contains(key) || key.StartsWith(CategoryOrderKeyPrefix, StringComparison.OrdinalIgnoreCase);

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

        [HttpGet("public")]
        [AllowAnonymous]
        public async Task<IActionResult> GetPublicSettings()
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return Ok(new List<SettingVm>());

            var settings = await _context.Database
                .SqlQueryRaw<SettingVm>("SELECT * FROM sp_get_settings({0})", companyId.Value)
                .ToListAsync();
            return Ok(settings.Where(s => IsPublicSettingKey(s.key)).ToList());
        }

        [HttpPut]
        public async Task<IActionResult> UpdateSettings([FromBody] List<CompanySettingDto> settingsList)
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest("Company context is required.");

            foreach (var s in settingsList)
            {
                await _context.Database.ExecuteSqlRawAsync(
                    "CALL sp_upsert_setting({0},{1},{2},{3})",
                    companyId.Value, s.Key, s.Value ?? "", s.GroupName ?? "GENERAL");
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
