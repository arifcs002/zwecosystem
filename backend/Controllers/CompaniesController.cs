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
    public class CompaniesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IFileTextLogger _fileLogger;

        public CompaniesController(ApplicationDbContext context, IFileTextLogger fileLogger)
        {
            _context = context;
            _fileLogger = fileLogger;
        }

        // Public endpoint for login page — returns minimal info by subdomain
        [HttpGet("public/{subdomain}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetPublicCompany(string subdomain)
        {
            var company = await _context.Companies
                .Where(c => c.Subdomain == subdomain.ToLower() && c.IsDeleted == 0)
                .Select(c => new { c.Id, c.Name, c.LogoUrl, c.Subdomain, c.IsActive })
                .FirstOrDefaultAsync();
            if (company == null) return NotFound();
            return Ok(company);
        }

        [HttpGet]
        public async Task<IActionResult> GetCompanies()
        {
            try
            {
                var companies = await _context.Database
                    .SqlQueryRaw<CompanyListVm>("SELECT * FROM sp_get_companies()")
                    .ToListAsync();
                return Ok(companies);
            }
            catch
            {
                // SP not yet deployed — fallback to direct query
                var companies = await _context.Companies
                    .IgnoreQueryFilters()
                    .Where(c => c.IsDeleted == 0)
                    .Select(c => new CompanyListVm
                    {
                        id             = c.Id,
                        name           = c.Name,
                        subdomain      = c.Subdomain,
                        logoUrl        = c.LogoUrl,
                        contactEmail   = c.ContactEmail,
                        contactPhone   = c.ContactPhone,
                        companyMobile  = c.CompanyMobile,
                        ownerName      = c.OwnerName,
                        ownerMobile    = c.OwnerMobile,
                        division       = c.Division,
                        district       = c.District,
                        thana          = c.Thana,
                        address        = c.Address,
                        facebookLink   = c.FacebookLink,
                        instagramLink  = c.InstagramLink,
                        bkashNumber    = c.BkashNumber,
                        nagadNumber    = c.NagadNumber,
                        bankName       = c.BankName,
                        bankAccountName = c.BankAccountName,
                        deliveryCharge = c.DeliveryCharge,
                        isActive       = c.IsActive,
                        approvalStatus = c.ApprovalStatus,
                        createdAt      = c.CreatedDate
                    })
                    .OrderByDescending(c => c.createdAt)
                    .ToListAsync();
                return Ok(companies);
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetCompany(int id)
        {
            var company = await _context.Companies.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == id && c.IsDeleted == 0);
            if (company == null) return NotFound();
            return Ok(company);
        }

        [HttpPost]
        public async Task<IActionResult> CreateCompany([FromBody] Company company)
        {
            if (company == null) return BadRequest(new { message = "Company payload is required." });

            NormalizePayload(company);
            if (string.IsNullOrWhiteSpace(company.Name))      return BadRequest(new { message = "Company name is required." });
            if (string.IsNullOrWhiteSpace(company.Subdomain)) return BadRequest(new { message = "Company subdomain is required." });
            if (await _context.Companies.AnyAsync(c => c.Subdomain == company.Subdomain))
                return Conflict(new { message = $"Subdomain '{company.Subdomain}' is already taken." });

            company.CreatedDate = DateTime.UtcNow;
            company.UpdatedDate = DateTime.UtcNow;
            company.IsActive = true;
            if (string.IsNullOrWhiteSpace(company.ApprovalStatus)) company.ApprovalStatus = "Pending";

            _context.Companies.Add(company);
            try
            {
                await _context.SaveChangesAsync();
                return Ok(company);
            }
            catch (DbUpdateException ex)
            {
                return BadRequest(new { message = "Failed to create company.", detail = ex.GetBaseException().Message });
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateCompany(int id, [FromBody] Company dto)
        {
            var company = await _context.Companies.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == id);
            if (company == null) return NotFound();

            if (string.Equals(company.Subdomain, "zw", StringComparison.OrdinalIgnoreCase) && !User.IsInRole("superadmin"))
                return Forbid();

            NormalizePayload(dto);
            if (string.IsNullOrWhiteSpace(dto.Name))      return BadRequest(new { message = "Company name is required." });
            if (string.IsNullOrWhiteSpace(dto.Subdomain)) return BadRequest(new { message = "Company subdomain is required." });
            if (await _context.Companies.AnyAsync(c => c.Id != id && c.Subdomain == dto.Subdomain))
                return Conflict(new { message = $"Subdomain '{dto.Subdomain}' is already taken." });

            await _context.Database.ExecuteSqlRawAsync(
                "CALL sp_update_company({0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18},{19},{20},{21})",
                id, dto.Name, dto.Subdomain,
                dto.ContactEmail ?? "", dto.ContactPhone ?? "",
                dto.OwnerName ?? "", dto.OwnerMobile ?? "", dto.CompanyMobile ?? "",
                dto.Division ?? "", dto.District ?? "", dto.Thana ?? "", dto.Address ?? "",
                dto.FacebookLink ?? "", dto.InstagramLink ?? "",
                dto.BkashNumber ?? "", dto.NagadNumber ?? "",
                dto.BankName ?? "", dto.BankAccountName ?? "",
                dto.LogoUrl ?? "", dto.BannerUrl ?? "",
                dto.IsActive, dto.ApprovalStatus ?? "Pending");

            var updated = await _context.Companies.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == id);
            return Ok(updated);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteCompany(int id)
        {
            var company = await _context.Companies.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == id);
            if (company == null) return NotFound(new { message = "Company not found." });
            if (string.Equals(company.Subdomain, "zw", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "The platform root company cannot be deleted." });

            await _context.Database.ExecuteSqlRawAsync("CALL sp_delete_company({0})", id);
            return Ok(new { message = $"Company '{company.Name}' has been deleted." });
        }

        [HttpPatch("{id}/toggle-status")]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var result = await _context.Database
                .SqlQueryRaw<CompanyToggleVm>("SELECT * FROM sp_toggle_company_status({0})", id)
                .ToListAsync();
            var vm = result.FirstOrDefault();
            if (vm == null) return NotFound(new { message = "Company not found." });
            return Ok(vm);
        }

        private static void NormalizePayload(Company c)
        {
            c.Name      = Trim(c.Name, 255);
            c.Subdomain = Trim(c.Subdomain, 100).ToLowerInvariant();
            c.ContactEmail   = TrimNull(c.ContactEmail, 255);
            c.ContactPhone   = TrimNull(c.ContactPhone, 50);
            c.OwnerName      = TrimNull(c.OwnerName, 255);
            c.OwnerMobile    = TrimNull(c.OwnerMobile, 50);
            c.CompanyMobile  = TrimNull(c.CompanyMobile, 50);
            c.Division       = TrimNull(c.Division, 100);
            c.District       = TrimNull(c.District, 100);
            c.Thana          = TrimNull(c.Thana, 100);
            c.Address        = string.IsNullOrWhiteSpace(c.Address) ? null : c.Address.Trim();
            c.FacebookLink   = TrimNull(c.FacebookLink, 500);
            c.InstagramLink  = TrimNull(c.InstagramLink, 500);
            c.BkashNumber    = TrimNull(c.BkashNumber, 50);
            c.NagadNumber    = TrimNull(c.NagadNumber, 50);
            c.BankName       = TrimNull(c.BankName, 255);
            c.BankAccountName = TrimNull(c.BankAccountName, 255);
            c.LogoUrl        = string.IsNullOrWhiteSpace(c.LogoUrl) ? null : c.LogoUrl.Trim();
            c.BannerUrl      = string.IsNullOrWhiteSpace(c.BannerUrl) ? null : c.BannerUrl.Trim();
            c.ApprovalStatus = TrimNull(c.ApprovalStatus, 50) ?? "Pending";
        }

        private static string Trim(string? v, int max)
        {
            var s = v?.Trim() ?? string.Empty;
            return s.Length > max ? s[..max] : s;
        }
        private static string? TrimNull(string? v, int max)
        {
            if (string.IsNullOrWhiteSpace(v)) return null;
            var s = v.Trim();
            return s.Length > max ? s[..max] : s;
        }
    }
}
