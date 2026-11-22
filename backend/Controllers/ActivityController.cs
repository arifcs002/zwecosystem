using Ecommerce.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Api.Controllers
{
    // Owner-only views: who did what (audit log) and which staff devices are logged in (sessions).
    [ApiController]
    [Route("api/activity")]
    [Authorize]
    public class ActivityController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        public ActivityController(ApplicationDbContext context) { _context = context; }

        private bool IsOwner => User.IsInRole("companyadmin") || User.IsInRole("superadmin") || User.IsInRole("super_admin");

        [HttpGet("log")]
        public async Task<IActionResult> Log([FromQuery] string? action, [FromQuery] int? userId, [FromQuery] string? from, [FromQuery] string? to, [FromQuery] int take = 200)
        {
            if (!IsOwner) return Forbid();
            if (!_context.CompanyId.HasValue) return BadRequest(new { message = "Company context is required." });

            var q = _context.AuditLogs.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(action)) q = q.Where(a => a.Action.StartsWith(action));
            if (userId.HasValue) q = q.Where(a => a.UserId == userId);
            if (ExpensesController.TryDate(from, out var f)) q = q.Where(a => a.CreatedDate >= DateTime.SpecifyKind(f.AddHours(-6), DateTimeKind.Utc));
            if (ExpensesController.TryDate(to, out var t)) q = q.Where(a => a.CreatedDate < DateTime.SpecifyKind(t.AddDays(1).AddHours(-6), DateTimeKind.Utc));

            var rows = await q.OrderByDescending(a => a.CreatedDate).Take(Math.Clamp(take, 1, 500))
                .Select(a => new { a.Id, a.Action, a.Details, a.UserId, a.IpAddress, date = a.CreatedDate }).ToListAsync();
            var userIds = rows.Where(r => r.UserId != null).Select(r => r.UserId!.Value).Distinct().ToList();
            var users = await _context.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Email);
            return Ok(rows.Select(r => new { r.Id, r.Action, r.Details, r.UserId, user = r.UserId != null ? users.GetValueOrDefault(r.UserId.Value) : null, r.IpAddress, r.date }));
        }

        [HttpGet("sessions")]
        public async Task<IActionResult> Sessions()
        {
            if (!IsOwner) return Forbid();
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest(new { message = "Company context is required." });

            // UserSession has no tenant filter, so scope it explicitly.
            var me = _context.CurrentUserId;
            var rows = await _context.UserSessions.AsNoTracking()
                .Where(s => s.CompanyId == companyId && s.IsActive && s.ExpiresAt > DateTime.UtcNow)
                .OrderByDescending(s => s.CreatedAt)
                .Select(s => new { s.Id, s.UserId, s.Email, s.FullName, s.UserType, s.IpAddress, s.UserAgent, s.CreatedAt, s.ExpiresAt, current = s.UserId == me })
                .ToListAsync();
            return Ok(rows);
        }

        [HttpDelete("sessions/{id}")]
        public async Task<IActionResult> Revoke(int id)
        {
            if (!IsOwner) return Forbid();
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest(new { message = "Company context is required." });

            var s = await _context.UserSessions.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId);
            if (s == null) return NotFound();
            if (s.UserId == _context.CurrentUserId) return BadRequest(new { message = "You can't sign yourself out here — use Log out." });
            s.IsActive = false;
            await _context.SaveChangesAsync();
            await Audit.LogAsync(_context, HttpContext, "session.revoke", s.Email);
            return Ok(new { message = "Signed out." });
        }
    }
}
