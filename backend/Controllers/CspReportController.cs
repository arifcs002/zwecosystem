using System.Threading;
using Ecommerce.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ecommerce.Api.Controllers
{
    // Browsers post here when a page breaks the (report-only) Content-Security-Policy, so the allow-list can be
    // checked against real traffic before it is enforced. Public by nature, so it is tiny, capped and throttled.
    [ApiController]
    [Route("api/csp-report")]
    [AllowAnonymous]
    public class CspReportController : ControllerBase
    {
        private static long _windowStart;
        private static int _count;
        private readonly IFileTextLogger _log;
        public CspReportController(IFileTextLogger log) { _log = log; }

        [HttpPost]
        [Consumes("application/csp-report", "application/reports+json", "application/json")]
        [RequestSizeLimit(8192)]
        public async Task<IActionResult> Report()
        {
            // at most 60 reports a minute for the whole server: enough to learn from, useless for log flooding
            var minute = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
            if (Interlocked.Exchange(ref _windowStart, minute) != minute) Interlocked.Exchange(ref _count, 0);
            if (Interlocked.Increment(ref _count) > 60) return NoContent();

            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();
            body = new string(body.Where(c => c >= ' ').ToArray());
            if (body.Length > 1500) body = body[..1500];
            _log.LogInfo("csp-report", $"{Request.Headers["Host"]} {body}");
            return NoContent();
        }
    }
}
