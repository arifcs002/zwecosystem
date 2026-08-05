using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Distributed;

namespace Ecommerce.Api.Controllers
{
    // Serves uploaded images from Redis when the physical file isn't on this
    // machine's disk. UseStaticFiles (Program.cs) runs first and serves
    // straight from wwwroot/uploads when the file IS present — this
    // controller only ever gets hit as the fallback for that miss, which is
    // exactly the case where local dev and production (or two different
    // backend containers) don't share a disk but do share the same Redis.
    [ApiController]
    [Route("uploads")]
    [AllowAnonymous]
    public class ImagesController : ControllerBase
    {
        private readonly IDistributedCache _cache;

        public ImagesController(IDistributedCache cache)
        {
            _cache = cache;
        }

        [HttpGet("{folder}/{fileName}")]
        public async Task<IActionResult> Get(string folder, string fileName)
        {
            var bytes = await _cache.GetAsync(UploadController.CacheKey(folder, fileName));
            if (bytes == null) return NotFound();

            // Uploaded files are content-hash named — the same URL always
            // serves the same bytes, safe to cache forever on the client.
            Response.Headers["Cache-Control"] = "public,max-age=31536000,immutable";
            return File(bytes, "image/jpeg");
        }
    }
}
