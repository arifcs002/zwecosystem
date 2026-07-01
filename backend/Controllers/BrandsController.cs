using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ecommerce.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;

namespace Ecommerce.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class BrandsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public BrandsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // Anonymous-safe brand list for the storefront brand carousel. Tenant is
        // resolved from the X-Tenant-ID header via the global query filter.
        [HttpGet("public")]
        [AllowAnonymous]
        public async Task<IActionResult> GetPublicBrands()
        {
            if (!_context.CompanyId.HasValue) return Ok(new List<object>());

            var brands = await _context.Brands
                .AsNoTracking()
                .OrderBy(b => b.Name)
                .Select(b => new { b.Id, b.Name, b.LogoUrl })
                .ToListAsync();
            return Ok(brands);
        }

        [HttpGet]
        public async Task<IActionResult> GetBrands()
        {
            var brands = await _context.Brands
                .AsNoTracking()
                .OrderBy(b => b.Name)
                .Select(b => new { b.Id, b.Name, b.LogoUrl, b.Description })
                .ToListAsync();
            return Ok(brands);
        }
    }
}
