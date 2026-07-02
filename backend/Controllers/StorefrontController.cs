using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ecommerce.Api.Infrastructure;
using Ecommerce.Api.Models;
using Microsoft.AspNetCore.Authorization;

namespace Ecommerce.Api.Controllers
{
    // Single aggregate endpoint for the public storefront homepage. Resolves the
    // tenant from the slug server-side and returns everything the shop needs in
    // ONE round trip (company + public settings + products + categories + brands),
    // instead of the client doing a company lookup and then four dependent calls.
    [ApiController]
    [Route("api/[controller]")]
    [AllowAnonymous]
    public class StorefrontController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public StorefrontController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet("{slug}")]
        public async Task<IActionResult> GetStorefront(string slug)
        {
            var company = await _context.Companies
                .Where(c => c.Subdomain == slug.ToLower() && c.IsDeleted == 0)
                .Select(c => new { c.Id, c.Name, c.LogoUrl, c.Subdomain, c.IsActive })
                .FirstOrDefaultAsync();
            if (company == null) return NotFound(new { message = "Store not found." });

            var cid = company.Id;

            // Company-scoped tables are behind a global query filter keyed on the
            // (header-derived) tenant, which isn't set for an anonymous slug call —
            // so ignore it and filter explicitly by the resolved company id.
            var products = await _context.Products
                .IgnoreQueryFilters().AsNoTracking()
                .Include(p => p.Category)
                .Where(p => p.IsDeleted == 0 && p.CompanyId == cid)
                .OrderByDescending(p => p.CreatedDate)
                .Select(p => new
                {
                    p.Id, p.Name, p.Price, p.CompareAtPrice, p.ImageUrl,
                    p.StockQuantity, p.CategoryId, p.CreatedDate,
                    Category = p.Category != null ? new { p.Category.Name } : null
                })
                .ToListAsync();

            var categories = await _context.Categories
                .IgnoreQueryFilters().AsNoTracking()
                .Where(c => c.IsDeleted == 0 && c.CompanyId == cid)
                .Select(c => new { c.Id, c.Name, c.ParentId, c.Sizes })
                .ToListAsync();

            var brands = await _context.Brands
                .IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.IsDeleted == 0 && x.CompanyId == cid)
                .OrderBy(x => x.Name)
                .Select(x => new { x.Id, x.Name, x.LogoUrl })
                .ToListAsync();

            var allSettings = await _context.Database
                .SqlQueryRaw<SettingVm>("SELECT * FROM sp_get_settings({0})", cid)
                .ToListAsync();
            var settings = allSettings.Where(s => SettingsController.IsPublicSettingKey(s.key)).ToList();

            return Ok(new { company, settings, products, categories, brands });
        }
    }
}
