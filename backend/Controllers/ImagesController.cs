using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Distributed;

namespace Ecommerce.Api.Controllers
{
    // Serves every uploaded image straight from Redis — there is no disk copy
    // (see UploadController). Redis runs standalone on the VPS host, outside
    // docker-compose entirely, so it isn't wiped by a redeploy the way a
    // container's disk would be, and both local dev and production read the
    // exact same instance.
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
