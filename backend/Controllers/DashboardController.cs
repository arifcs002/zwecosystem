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
    public class DashboardController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IFileTextLogger _fileLogger;

        public DashboardController(ApplicationDbContext context, IFileTextLogger fileLogger)
        {
            _context = context;
            _fileLogger = fileLogger;
        }

        [HttpGet("stats")]
        public async Task<IActionResult> GetStats([FromQuery] int? companyId)
        {
            var cid = ResolveCompanyId(companyId);
            if (cid == null)
                return Ok(new DashboardStatsVm());

            var stats = await _context.Database
                .SqlQueryRaw<DashboardStatsVm>("SELECT * FROM sp_get_dashboard_stats({0})", cid.Value)
                .ToListAsync();

            return Ok(stats.FirstOrDefault() ?? new DashboardStatsVm());
        }

        [HttpGet("sales-chart")]
        public async Task<IActionResult> GetSalesChart([FromQuery] int? companyId, [FromQuery] int days = 7)
        {
            var cid = ResolveCompanyId(companyId);
            if (cid == null) return Ok(new List<object>());

            var chart = await _context.Database
                .SqlQueryRaw<SalesChartVm>("SELECT * FROM sp_get_sales_chart({0},{1})", cid.Value, days)
                .ToListAsync();

            return Ok(chart);
        }

        [HttpGet("top-products")]
        public async Task<IActionResult> GetTopProducts([FromQuery] int? companyId, [FromQuery] int limit = 5)
        {
            var cid = ResolveCompanyId(companyId);
            if (cid == null) return Ok(new List<object>());

            var topProducts = await _context.Database
                .SqlQueryRaw<TopProductVm>("SELECT * FROM sp_get_top_products({0},{1})", cid.Value, limit)
                .ToListAsync();

            return Ok(topProducts);
        }

        [HttpGet("recent-orders")]
        public async Task<IActionResult> GetRecentOrders([FromQuery] int? companyId, [FromQuery] int limit = 10)
        {
            var cid = ResolveCompanyId(companyId);
            if (cid == null) return Ok(new List<object>());

            var orders = await _context.Database
                .SqlQueryRaw<RecentOrderVm>("SELECT * FROM sp_get_recent_orders({0},{1})", cid.Value, limit)
                .ToListAsync();

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
