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

        public AppVersionsController(ApplicationDbContext context, IWebHostEnvironment env, IFileTextLogger fileLogger)
        {
            _context = context;
            _env = env;
            _fileLogger = fileLogger;
        }

        // Any authenticated session (super admin or company staff) can fetch the
        // current release — there's no customer/public login into the workspace
        // app, so requiring auth here already excludes anonymous visitors.
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

        [HttpGet]
        [Authorize(Roles = "superadmin")]
        public async Task<IActionResult> GetAll()
        {
            var versions = await _context.AppVersions
                .OrderByDescending(v => v.VersionCode)
                .ToListAsync();
            return Ok(versions);
        }

        [HttpPost]
        [Authorize(Roles = "superadmin")]
        [RequestSizeLimit(300_000_000)]
        public async Task<IActionResult> Upload(IFormFile file, [FromForm] string versionName, [FromForm] int versionCode, [FromForm] string? releaseNotes)
        {
            if (file == null || file.Length == 0) return BadRequest("No APK file uploaded.");
            if (string.IsNullOrWhiteSpace(versionName)) return BadRequest("Version name is required.");

            var wwwRoot = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            var appsFolder = Path.Combine(wwwRoot, "apks");
            if (!Directory.Exists(appsFolder)) Directory.CreateDirectory(appsFolder);

            byte[] bytes;
            using (var ms = new MemoryStream())
            {
                await file.CopyToAsync(ms);
                bytes = ms.ToArray();
            }

            // Content-hash filename — same reasoning as product image uploads:
            // identical bytes always resolve to the identical URL, so the client
            // can cache a downloaded APK forever and only re-fetch on a real change.
            var hash = Convert.ToHexString(SHA256.HashData(bytes))[..16].ToLowerInvariant();
            var fileName = $"{hash}.apk";
            var filePath = Path.Combine(appsFolder, fileName);
            if (!System.IO.File.Exists(filePath))
                await System.IO.File.WriteAllBytesAsync(filePath, bytes);

            var apkUrl = $"/apks/{fileName}";

            // Only one release is "latest" at a time.
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
}
