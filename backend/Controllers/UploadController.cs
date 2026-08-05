using Microsoft.AspNetCore.Mvc;
using Ecommerce.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Distributed;
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

        private readonly IFileTextLogger _fileLogger;
        private readonly IDistributedCache _cache;

        public UploadController(IFileTextLogger fileLogger, IDistributedCache cache)
        {
            _fileLogger = fileLogger;
            _cache = cache;
        }

        // Same key scheme ImagesController reads from.
        internal static string CacheKey(string folder, string fileName) => $"img:{folder}:{fileName}";

        [HttpPost]
        public async Task<IActionResult> UploadFile(IFormFile file, [FromQuery] string folder = "other")
        {
            if (file == null || file.Length == 0) return BadRequest("No file uploaded");

            // Whitelist allowed subfolders
            var allowed = new[] { "product", "logo", "other" };
            if (!allowed.Contains(folder)) folder = "other";

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

            // Redis only, no disk — a standalone Redis on the VPS host (not a
            // docker container) is the single source of truth, so local dev
            // and production see the exact same images with nothing to lose
            // on a redeploy (disk inside a container/image doesn't persist;
            // this Redis instance isn't managed by docker-compose at all).
            // Never expires: identical content always resolves to the same
            // content-hash key, so there's nothing to invalidate.
            await _cache.SetAsync(CacheKey(folder, uniqueFileName), outputBytes, new DistributedCacheEntryOptions());

            // Return relative path — Angular resolveImageUrl() prepends siteUrl
            // (port 85 via nginx) so the URL works on any host, not just localhost.
            return Ok(new { imageUrl = $"/uploads/{folder}/{uniqueFileName}" });
        }
    }
}
