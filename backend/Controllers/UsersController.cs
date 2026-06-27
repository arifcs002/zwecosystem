using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ecommerce.Api.Infrastructure;
using Ecommerce.Api.Models;
using Microsoft.AspNetCore.Authorization;

namespace Ecommerce.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class UsersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IFileTextLogger _fileLogger;

        public UsersController(ApplicationDbContext context, IFileTextLogger fileLogger)
        {
            _context = context;
            _fileLogger = fileLogger;
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetUser(int id)
        {
            var isSuperAdmin = User.IsInRole("superadmin");
            var companyId    = _context.CompanyId;

            var user = isSuperAdmin
                ? await _context.Users.IgnoreQueryFilters()
                    .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                    .FirstOrDefaultAsync(u => u.Id == id)
                : await _context.Users
                    .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                    .FirstOrDefaultAsync(u => u.Id == id);

            if (user == null) return NotFound();
            if (!isSuperAdmin && user.CompanyId != companyId) return Forbid();

            return Ok(new {
                user.Id, user.Email, user.FirstName, user.LastName,
                user.PhoneNumber, user.IsActive, user.CompanyId,
                Roles = user.UserRoles.Where(ur => ur.IsDeleted == 0).Select(ur => ur.Role!.Name).ToList()
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetUsers()
        {
            var isSuperAdmin = User.IsInRole("superadmin");
            var companyId    = _context.CompanyId ?? 0;

            var rawUsers = await _context.Database
                .SqlQueryRaw<UserListVm>(
                    "SELECT * FROM sp_get_users({0},{1})", companyId, isSuperAdmin)
                .ToListAsync();

            // Reshape roles from comma-string to list
            var list = rawUsers.Select(u => new {
                u.Id, u.Email, u.FirstName, u.LastName, u.PhoneNumber,
                u.IsActive, u.CompanyId,
                Roles = u.Roles.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList()
            });

            return Ok(list);
        }

        [HttpGet("roles")]
        public async Task<IActionResult> GetRoles()
        {
            var roles = await _context.Database
                .SqlQueryRaw<RoleVm>("SELECT * FROM sp_get_roles()")
                .ToListAsync();
            return Ok(roles);
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> CreateUser([FromBody] CreateUserDto dto)
        {
            var isSuperAdmin = User.IsInRole("superadmin");
            var companyId    = _context.CompanyId;

            if (!isSuperAdmin)
            {
                if (!companyId.HasValue) return Forbid();
                if (dto.CompanyId != companyId.Value) return Forbid();
            }

            if (await _context.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == dto.Email))
                return BadRequest(new { message = "Email already exists" });

            var role         = await _context.Roles.FirstOrDefaultAsync(r => r.Name == dto.Role);
            var passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);

            var userId = (await _context.Database.SqlQueryRaw<int>(
                "SELECT sp_create_user({0},{1},{2},{3},{4},{5},{6},{7},{8},{9})",
                dto.CompanyId, dto.Email, passwordHash, dto.FirstName, dto.LastName,
                dto.PhoneNumber, dto.Role, dto.IsActive, role?.Id, _context.CurrentUserId
            ).ToListAsync()).FirstOrDefault();

            return Ok(new { Id = userId, dto.Email, dto.FirstName, dto.LastName, dto.IsActive, dto.CompanyId });
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateUser(int id, [FromBody] UpdateUserDto dto)
        {
            var isSuperAdmin = User.IsInRole("superadmin");
            var companyId    = _context.CompanyId;

            var targetUser = isSuperAdmin
                ? await _context.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == id)
                : await _context.Users.FirstOrDefaultAsync(u => u.Id == id);

            if (targetUser == null) return NotFound("User not found");
            if (!isSuperAdmin && targetUser.CompanyId != companyId) return Forbid();

            var passwordHash    = !string.IsNullOrEmpty(dto.Password) ? BCrypt.Net.BCrypt.HashPassword(dto.Password) : null;
            var targetCompanyId = isSuperAdmin ? dto.CompanyId : targetUser.CompanyId;
            var newRole         = await _context.Roles.FirstOrDefaultAsync(r => r.Name == dto.Role);

            await _context.Database.ExecuteSqlRawAsync(
                "CALL sp_update_user({0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10})",
                id, dto.Email, dto.FirstName, dto.LastName, dto.PhoneNumber,
                dto.IsActive, dto.Role, passwordHash, targetCompanyId, newRole?.Id, _context.CurrentUserId);

            return Ok(new { Id = id, dto.Email, dto.FirstName, dto.LastName, dto.IsActive, CompanyId = targetCompanyId });
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteUser(int id)
        {
            var isSuperAdmin = User.IsInRole("superadmin");
            var companyId    = _context.CompanyId;

            var targetUser = isSuperAdmin
                ? await _context.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == id)
                : await _context.Users.FirstOrDefaultAsync(u => u.Id == id);

            if (targetUser == null) return NotFound("User not found");
            if (string.Equals(targetUser.Email, "arif", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "System user arif cannot be deleted." });
            if (!isSuperAdmin && targetUser.CompanyId != companyId) return Forbid();

            await _context.Database.ExecuteSqlRawAsync("CALL sp_delete_user({0},{1})", id, _context.CurrentUserId);
            return Ok(new { message = "User deleted successfully" });
        }

        [HttpPost("{userId}/admin-reset-password")]
        public async Task<IActionResult> AdminResetPassword(int userId, [FromBody] AdminResetPasswordDto dto)
        {
            if (!User.IsInRole("superadmin") && !User.IsInRole("companyadmin")) return Forbid();

            var companyId  = _context.CompanyId;
            var isSuperAdmin = User.IsInRole("superadmin");

            var targetUser = isSuperAdmin
                ? await _context.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId)
                : await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);

            if (targetUser == null) return NotFound("User not found");
            if (!isSuperAdmin && targetUser.CompanyId != companyId) return Forbid();
            if (dto.NewPassword != dto.ConfirmPassword)
                return BadRequest(new { message = "New password and confirm password do not match." });

            targetUser.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
            await _context.SaveChangesAsync();

            return Ok(new { message = $"Password for {targetUser.Email} updated successfully." });
        }
    }
}
