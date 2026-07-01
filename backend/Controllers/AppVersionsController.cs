using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ecommerce.Api.Domain;
using Ecommerce.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using System.Security.Cryptography;

namespace Ecommerce.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class AppVersionsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly IFileTextLogger _fileLogger;

        private static readonly string[] SuperAdminRoles = ["superadmin", "SUPERADMIN", "SuperAdmin"];

        public AppVersionsController(ApplicationDbContext context, IWebHostEnvironment env, IFileTextLogger fileLogger)
        {
            _context = context;
            _env = env;
            _fileLogger = fileLogger;
        }

        private bool IsSuperAdmin() =>
            SuperAdminRoles.Any(r => User.IsInRole(r));

        private string ApksFolder()
        {
            var wwwRoot = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            var folder = Path.Combine(wwwRoot, "uploads", "apks");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            return folder;
        }

        // ── GET /latest — any authenticated user (staff, super-admin) ──────────
        [HttpGet("latest")]
        public async Task<IActionResult> GetLatest()
        {
            var latest = await _context.AppVersions
                .Where(v => v.IsActive)
                .OrderByDescending(v => v.VersionCode)
                .FirstOrDefaultAsync();
            if (latest == null) return NotFound(new { message = "No app release published yet." });
            return Ok(latest);
        }

        // ── GET — all versions, super-admin only ───────────────────────────────
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            if (!IsSuperAdmin()) return Forbid();
            var versions = await _context.AppVersions
                .OrderByDescending(v => v.VersionCode)
                .ToListAsync();
            return Ok(versions);
        }

        // ── GET /server-files — list .apk files already on the server ─────────
        [HttpGet("server-files")]
        public IActionResult GetServerFiles()
        {
            if (!IsSuperAdmin()) return Forbid();
            var folder = ApksFolder();
            var files = Directory.GetFiles(folder, "*.apk")
                .Select(f => new
                {
                    name = Path.GetFileName(f),
                    sizeKb = (int)(new FileInfo(f).Length / 1024),
                    url = $"/uploads/apks/{Path.GetFileName(f)}"
                })
                .OrderByDescending(f => f.name)
                .ToList();
            return Ok(files);
        }

        // ── POST /register — register a file already in wwwroot/uploads/apks/ ─────────
        [HttpPost("register")]
        public async Task<IActionResult> RegisterExisting([FromBody] RegisterApkDto dto)
        {
            if (!IsSuperAdmin()) return Forbid();
            if (string.IsNullOrWhiteSpace(dto.FileName)) return BadRequest("File name is required.");
            if (string.IsNullOrWhiteSpace(dto.VersionName)) return BadRequest("Version name is required.");

            var folder = ApksFolder();
            var fullPath = Path.Combine(folder, Path.GetFileName(dto.FileName));
            if (!System.IO.File.Exists(fullPath))
                return BadRequest(new { message = $"File '{dto.FileName}' not found in /uploads/apks/ folder on server." });

            var apkUrl = $"/uploads/apks/{Path.GetFileName(dto.FileName)}";
            return await SaveVersion(dto.VersionName, dto.VersionCode, apkUrl, dto.ReleaseNotes);
        }

        // ── POST — upload APK from browser ────────────────────────────────────
        [HttpPost]
        [RequestSizeLimit(300_000_000)]
        public async Task<IActionResult> Upload(IFormFile file, [FromForm] string versionName, [FromForm] int versionCode, [FromForm] string? releaseNotes)
        {
            if (!IsSuperAdmin()) return Forbid();
            if (file == null || file.Length == 0) return BadRequest("No APK file uploaded.");
            if (string.IsNullOrWhiteSpace(versionName)) return BadRequest("Version name is required.");

            var folder = ApksFolder();
            byte[] bytes;
            using (var ms = new MemoryStream())
            {
                await file.CopyToAsync(ms);
                bytes = ms.ToArray();
            }

            var hash = Convert.ToHexString(SHA256.HashData(bytes))[..16].ToLowerInvariant();
            var fileName = $"{hash}.apk";
            var filePath = Path.Combine(folder, fileName);
            if (!System.IO.File.Exists(filePath))
                await System.IO.File.WriteAllBytesAsync(filePath, bytes);

            return await SaveVersion(versionName, versionCode, $"/uploads/apks/{fileName}", releaseNotes);
        }

        // ── PUT /{id} — edit version name / release notes ─────────────────────
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] AppVersionUpdateDto dto)
        {
            if (!IsSuperAdmin()) return Forbid();
            var version = await _context.AppVersions.FindAsync(id);
            if (version == null) return NotFound();

            if (!string.IsNullOrWhiteSpace(dto.VersionName)) version.VersionName = dto.VersionName;
            if (dto.VersionCode.HasValue) version.VersionCode = dto.VersionCode.Value;
            version.ReleaseNotes = dto.ReleaseNotes;
            await _context.SaveChangesAsync();
            return Ok(version);
        }

        // ── DELETE /{id} — remove record (+ file if deleteFile=true) ──────────
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, [FromQuery] bool deleteFile = false)
        {
            if (!IsSuperAdmin()) return Forbid();
            var version = await _context.AppVersions.FindAsync(id);
            if (version == null) return NotFound();

            if (deleteFile && !string.IsNullOrWhiteSpace(version.ApkUrl))
            {
                var wwwRoot = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                var filePath = Path.Combine(wwwRoot, version.ApkUrl.TrimStart('/'));
                if (System.IO.File.Exists(filePath)) System.IO.File.Delete(filePath);
            }

            _context.AppVersions.Remove(version);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Deleted." });
        }

        // ── shared helper ──────────────────────────────────────────────────────
        private async Task<IActionResult> SaveVersion(string versionName, int versionCode, string apkUrl, string? releaseNotes)
        {
            var previouslyActive = await _context.AppVersions.Where(v => v.IsActive).ToListAsync();
            foreach (var v in previouslyActive) v.IsActive = false;

            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value;

            var version = new AppVersion
            {
                VersionName = versionName,
                VersionCode = versionCode,
                ApkUrl = apkUrl,
                ReleaseNotes = releaseNotes,
                IsActive = true,
                CreatedDate = DateTime.UtcNow,
                UploadedByUserId = int.TryParse(userIdClaim, out var uid) ? uid : null
            };
            _context.AppVersions.Add(version);
            await _context.SaveChangesAsync();
            return Ok(version);
        }
    }

    public record RegisterApkDto(string FileName, string VersionName, int VersionCode, string? ReleaseNotes);
    public record AppVersionUpdateDto(string? VersionName, int? VersionCode, string? ReleaseNotes);
}
