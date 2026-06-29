using Microsoft.AspNetCore.Mvc;
using Ecommerce.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;

namespace Ecommerce.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class UploadController : ControllerBase
    {
        private readonly IWebHostEnvironment _env;
        private readonly IFileTextLogger _fileLogger;

        public UploadController(IWebHostEnvironment env, IFileTextLogger fileLogger)
        {
            _env = env;
            _fileLogger = fileLogger;
        }

        [HttpPost]
        public async Task<IActionResult> UploadFile(IFormFile file, [FromQuery] string folder = "other")
        {
            if (file == null || file.Length == 0) return BadRequest("No file uploaded");

            // Whitelist allowed subfolders
            var allowed = new[] { "product", "logo", "other" };
            if (!allowed.Contains(folder)) folder = "other";

            var baseUploads = Path.Combine(_env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"), "uploads");
            var uploadsFolder = Path.Combine(baseUploads, folder);
            if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

            var uniqueFileName = $"{Guid.NewGuid()}_{Path.GetFileName(file.FileName)}";
            var filePath = Path.Combine(uploadsFolder, uniqueFileName);

            using var stream = new FileStream(filePath, FileMode.Create);
            await file.CopyToAsync(stream);

            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            return Ok(new { imageUrl = $"{baseUrl}/uploads/{folder}/{uniqueFileName}" });
        }
    }
}
