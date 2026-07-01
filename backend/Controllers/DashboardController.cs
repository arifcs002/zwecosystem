using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ecommerce.Api.Infrastructure;
using Ecommerce.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace Ecommerce.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class DashboardController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IFileTextLogger _fileLogger;
        private readonly IDistributedCache _cache;
        private static readonly DistributedCacheEntryOptions CacheTtl = new()
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60)
        };

        public DashboardController(ApplicationDbContext context, IFileTextLogger fileLogger, IDistributedCache cache)
        {
            _context = context;
            _fileLogger = fileLogger;
            _cache = cache;
        }

        // Dashboard data changes with every order/sale, but tolerating up to 60s of
        // staleness is fine and turns repeat page visits into cache hits instead of
        // re-running the stored procedures every time.
        private async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory)
        {
            var cached = await _cache.GetStringAsync(key);
            if (cached != null)
            {
                var value = JsonSerializer.Deserialize<T>(cached);
                if (value != null) return value;
            }

            var result = await factory();
            await _cache.SetStringAsync(key, JsonSerializer.Serialize(result), CacheTtl);
            return result;
        }

        [HttpGet("stats")]
        public async Task<IActionResult> GetStats([FromQuery] int? companyId)
        {
            var cid = ResolveCompanyId(companyId);
            if (cid == null)
                return Ok(new DashboardStatsVm());

            var stats = await GetOrSetAsync($"dash:stats:{cid.Value}", async () =>
                (await _context.Database
                    .SqlQueryRaw<DashboardStatsVm>("SELECT * FROM sp_get_dashboard_stats({0})", cid.Value)
                    .ToListAsync()).FirstOrDefault() ?? new DashboardStatsVm());

            return Ok(stats);
        }

        [HttpGet("sales-chart")]
        public async Task<IActionResult> GetSalesChart([FromQuery] int? companyId, [FromQuery] int days = 7)
        {
            var cid = ResolveCompanyId(companyId);
            if (cid == null) return Ok(new List<object>());

            var chart = await GetOrSetAsync($"dash:chart:{cid.Value}:{days}", () =>
                _context.Database
                    .SqlQueryRaw<SalesChartVm>("SELECT * FROM sp_get_sales_chart({0},{1})", cid.Value, days)
                    .ToListAsync());

            return Ok(chart);
        }

        [HttpGet("top-products")]
        public async Task<IActionResult> GetTopProducts([FromQuery] int? companyId, [FromQuery] int limit = 5)
        {
            var cid = ResolveCompanyId(companyId);
            if (cid == null) return Ok(new List<object>());

            var topProducts = await GetOrSetAsync($"dash:top:{cid.Value}:{limit}", () =>
                _context.Database
                    .SqlQueryRaw<TopProductVm>("SELECT * FROM sp_get_top_products({0},{1})", cid.Value, limit)
                    .ToListAsync());

            return Ok(topProducts);
        }

        [HttpGet("recent-orders")]
        public async Task<IActionResult> GetRecentOrders([FromQuery] int? companyId, [FromQuery] int limit = 10)
        {
            var cid = ResolveCompanyId(companyId);
            if (cid == null) return Ok(new List<object>());

            var orders = await GetOrSetAsync($"dash:recent:{cid.Value}:{limit}", () =>
                _context.Database
                    .SqlQueryRaw<RecentOrderVm>("SELECT * FROM sp_get_recent_orders({0},{1})", cid.Value, limit)
                    .ToListAsync());

            return Ok(orders);
        }

        private int? ResolveCompanyId(int? queryCompanyId)
        {
            if (User.IsInRole("superadmin") && queryCompanyId.HasValue && queryCompanyId.Value > 0)
                return queryCompanyId;

            var claim = User.FindFirst("companyId")?.Value ?? User.FindFirst("company_id")?.Value;
            return int.TryParse(claim, out var cid) ? cid : null;
        }
    }
}
