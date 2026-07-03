using Microsoft.AspNetCore.Mvc;
using Ecommerce.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using System.Security.Cryptography;

namespace Ecommerce.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class UploadController : ControllerBase
    {
        private const int MaxDimension = 1200; // px — product/logo photos never need to be larger than this on screen
        private static readonly JpegEncoder JpegEncoder = new() { Quality = 82 };

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

            byte[] outputBytes;
            bool reencoded;
            try
            {
                using var image = await Image.LoadAsync(file.OpenReadStream());
                if (image.Width > MaxDimension || image.Height > MaxDimension)
                {
                    image.Mutate(x => x.Resize(new ResizeOptions
                    {
                        Mode = ResizeMode.Max,
                        Size = new Size(MaxDimension, MaxDimension)
                    }));
                }
                using var ms = new MemoryStream();
                await image.SaveAsJpegAsync(ms, JpegEncoder);
                outputBytes = ms.ToArray();
                reencoded = true;
            }
            catch (UnknownImageFormatException)
            {
                // This endpoint is only ever meant to receive images. If the bytes
                // can't be decoded as one, reject rather than storing an arbitrary
                // file — otherwise an .html/.svg with embedded script could be
                // uploaded and then served same-origin as a stored-XSS payload.
                return BadRequest(new { message = "Only image files are allowed." });
            }

            // Name the file after a hash of its own bytes: identical content always
            // resolves to the identical URL, and any content change produces a new
            // URL automatically — that's what lets us cache these responses on the
            // client (and CDN) forever instead of re-validating on every request.
            var hash = Convert.ToHexString(SHA256.HashData(outputBytes))[..16].ToLowerInvariant();
            var ext = reencoded ? ".jpg" : Path.GetExtension(file.FileName);
            if (string.IsNullOrEmpty(ext)) ext = ".bin";
            var uniqueFileName = $"{hash}{ext}";
            var filePath = Path.Combine(uploadsFolder, uniqueFileName);

            if (!System.IO.File.Exists(filePath))
                await System.IO.File.WriteAllBytesAsync(filePath, outputBytes);

            // Return relative path — Angular resolveImageUrl() prepends siteUrl
            // (port 85 via nginx) so the URL works on any host, not just localhost.
            return Ok(new { imageUrl = $"/uploads/{folder}/{uniqueFileName}" });
        }
    }
}
