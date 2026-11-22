using Ecommerce.Api.Domain;
using Ecommerce.Api.Infrastructure;
using Ecommerce.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Api.Controllers
{
    // Admin side of storefront customers: groups with an automatic member discount, the list of
    // registered shoppers, and assigning a shopper to a group. (Tenant-scoped by the query filters.)
    [ApiController]
    [Route("api/customer-groups")]
    [Authorize]
    public class CustomerGroupsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        public CustomerGroupsController(ApplicationDbContext context) { _context = context; }

        [HttpGet]
        public async Task<IActionResult> Groups()
        {
            var groups = await _context.CustomerGroups.AsNoTracking().OrderBy(g => g.Name).ToListAsync();
            var counts = await _context.CustomerAccounts.AsNoTracking().Where(a => a.GroupId != null)
                .GroupBy(a => a.GroupId).Select(g => new { id = g.Key, n = g.Count() }).ToListAsync();
            return Ok(groups.Select(g => new
            {
                g.Id, g.Name, g.Description, g.DiscountPercent,
                members = counts.FirstOrDefault(c => c.id == g.Id)?.n ?? 0
            }));
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] GroupUpsertDto dto)
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest(new { message = "Company context is required." });
            var error = Check(dto);
            if (error != null) return BadRequest(new { message = error });
            var name = dto.Name.Trim();
            if (await _context.CustomerGroups.AnyAsync(g => g.Name.ToLower() == name.ToLower()))
                return BadRequest(new { message = $"A group named '{name}' already exists." });

            var g = new CustomerGroup { CompanyId = companyId.Value, Name = name, CreatedBy = _context.CurrentUserId };
            Apply(g, dto);
            _context.CustomerGroups.Add(g);
            await _context.SaveChangesAsync();
            await Audit.LogAsync(_context, HttpContext, "customer-group.create", $"{g.Name} ({g.DiscountPercent:0.##}%)");
            return Ok(new { g.Id });
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] GroupUpsertDto dto)
        {
            var g = await _context.CustomerGroups.FirstOrDefaultAsync(x => x.Id == id);
            if (g == null) return NotFound();
            var error = Check(dto);
            if (error != null) return BadRequest(new { message = error });
            var name = dto.Name.Trim();
            if (await _context.CustomerGroups.AnyAsync(x => x.Id != id && x.Name.ToLower() == name.ToLower()))
                return BadRequest(new { message = $"A group named '{name}' already exists." });
            Apply(g, dto);
            g.UpdatedBy = _context.CurrentUserId;
            g.UpdatedDate = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            await Audit.LogAsync(_context, HttpContext, "customer-group.update", $"{g.Name} ({g.DiscountPercent:0.##}%)");
            return Ok(new { g.Id });
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var g = await _context.CustomerGroups.FirstOrDefaultAsync(x => x.Id == id);
            if (g == null) return NotFound();
            g.IsDeleted = 1;
            g.DeletedDate = DateTime.UtcNow;
            g.DeletedBy = _context.CurrentUserId;
            await _context.CustomerAccounts.Where(a => a.GroupId == id).ExecuteUpdateAsync(s => s.SetProperty(a => a.GroupId, (int?)null));
            await _context.SaveChangesAsync();
            await Audit.LogAsync(_context, HttpContext, "customer-group.delete", g.Name);
            return Ok(new { message = "Group deleted." });
        }

        // ── registered shoppers ──────────────────────────────────────────────────
        [HttpGet("accounts")]
        public async Task<IActionResult> Accounts([FromQuery] string? search)
        {
            var q = _context.CustomerAccounts.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                q = q.Where(a => a.Phone.Contains(s) || EF.Functions.ILike(a.Name, $"%{s}%"));
            }
            var rows = await q.OrderByDescending(a => a.CreatedDate).Take(500).ToListAsync();
            var groupNames = await _context.CustomerGroups.AsNoTracking().ToDictionaryAsync(g => g.Id, g => g.Name);
            var orderCounts = await _context.Orders.AsNoTracking().Where(o => o.CustomerAccountId != null)
                .GroupBy(o => o.CustomerAccountId).Select(g => new { id = g.Key, n = g.Count() }).ToListAsync();
            return Ok(rows.Select(a => new
            {
                a.Id, a.Name, a.Phone, a.Email, a.IsActive, a.GroupId,
                groupName = a.GroupId != null ? groupNames.GetValueOrDefault(a.GroupId.Value) : null,
                orders = orderCounts.FirstOrDefault(o => o.id == a.Id)?.n ?? 0,
                a.CreatedDate, a.LastLogin
            }));
        }

        [HttpPut("accounts/{id}/group")]
        public async Task<IActionResult> AssignGroup(int id, [FromBody] AccountGroupDto dto)
        {
            var a = await _context.CustomerAccounts.FirstOrDefaultAsync(x => x.Id == id);
            if (a == null) return NotFound();
            if (dto.GroupId != null && !await _context.CustomerGroups.AnyAsync(g => g.Id == dto.GroupId))
                return BadRequest(new { message = "That group doesn't exist." });   // a group from another shop is invisible here
            a.GroupId = dto.GroupId;
            a.UpdatedDate = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            await Audit.LogAsync(_context, HttpContext, "customer.group", $"{a.Phone} -> group {dto.GroupId?.ToString() ?? "none"}");
            return Ok(new { a.Id, a.GroupId });
        }

        [HttpPut("accounts/{id}/active")]
        public async Task<IActionResult> SetActive(int id, [FromBody] bool isActive)
        {
            var a = await _context.CustomerAccounts.FirstOrDefaultAsync(x => x.Id == id);
            if (a == null) return NotFound();
            a.IsActive = isActive;
            a.UpdatedDate = DateTime.UtcNow;
            if (!isActive) await _context.CustomerSessions.Where(s => s.AccountId == id).ExecuteDeleteAsync();   // sign them out now
            await _context.SaveChangesAsync();
            await Audit.LogAsync(_context, HttpContext, isActive ? "customer.enable" : "customer.disable", a.Phone);
            return Ok(new { a.Id, a.IsActive });
        }

        private static string? Check(GroupUpsertDto dto)
        {
            var name = (dto.Name ?? "").Trim();
            if (name.Length < 2 || name.Length > 60) return "Group name must be 2-60 characters.";
            if (dto.DiscountPercent < 0 || dto.DiscountPercent > 100) return "Discount must be between 0 and 100 percent.";
            if ((dto.Description?.Length ?? 0) > 300) return "Description is too long.";
            return null;
        }

        private static void Apply(CustomerGroup g, GroupUpsertDto dto)
        {
            g.Name = dto.Name.Trim();
            g.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
            g.DiscountPercent = Math.Round(dto.DiscountPercent, 2);
        }
    }
}
