using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ecommerce.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;

namespace Ecommerce.Api.Controllers
{
    // Storefront customers are not "users" — they check out as guests with just
    // a name + mobile number. This exposes them as a CRM-lite list derived from
    // their orders (grouped by phone). Tenant scoping comes for free from the
    // Order query filter on _context.CompanyId.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class CustomersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public CustomersController(ApplicationDbContext context)
        {
            _context = context;
        }

        // ── GET — aggregated customer list (one row per phone number) ──────────
        [HttpGet]
        public async Task<IActionResult> GetCustomers([FromQuery] string? search = null)
        {
            var query = _context.Orders
                .Where(o => o.SaleType == "ECOMMERCE"
                            && o.CustomerPhone != null && o.CustomerPhone != "");

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                query = query.Where(o => o.CustomerPhone!.Contains(s)
                                         || (o.CustomerName != null && EF.Functions.ILike(o.CustomerName, $"%{s}%")));
            }

            var customers = await query
                .GroupBy(o => o.CustomerPhone)
                .Select(g => new
                {
                    phone        = g.Key,
                    name         = g.OrderByDescending(o => o.CreatedDate).Select(o => o.CustomerName).FirstOrDefault(),
                    lastAddress  = g.OrderByDescending(o => o.CreatedDate).Select(o => o.ShippingAddress).FirstOrDefault(),
                    lastDistrict = g.OrderByDescending(o => o.CreatedDate).Select(o => o.ShippingDistrict).FirstOrDefault(),
                    totalOrders  = g.Count(),
                    // "Cancelled" orders don't count toward what a customer is worth.
                    totalSpent   = g.Where(o => o.Status != "CANCELLED").Sum(o => o.Total),
                    lastOrderAt  = g.Max(o => o.CreatedDate)
                })
                .OrderByDescending(c => c.lastOrderAt)
                .ToListAsync();

            return Ok(customers);
        }

        // ── GET /{phone} — one customer's profile + order history ──────────────
        [HttpGet("{phone}")]
        public async Task<IActionResult> GetCustomer(string phone)
        {
            var orders = await _context.Orders
                .Where(o => o.SaleType == "ECOMMERCE" && o.CustomerPhone == phone)
                .OrderByDescending(o => o.CreatedDate)
                .Select(o => new
                {
                    o.Id, o.OrderNumber, o.Status, o.Total, o.PaymentMethod,
                    o.ShippingAddress, o.ShippingDistrict, o.ShippingThana,
                    o.CreatedDate
                })
                .ToListAsync();

            if (orders.Count == 0) return NotFound(new { message = "No customer found for this number." });

            var profile = new
            {
                phone,
                name = await _context.Orders
                    .Where(o => o.SaleType == "ECOMMERCE" && o.CustomerPhone == phone)
                    .OrderByDescending(o => o.CreatedDate)
                    .Select(o => o.CustomerName)
                    .FirstOrDefaultAsync(),
                totalOrders = orders.Count,
                totalSpent  = orders.Where(o => o.Status != "CANCELLED").Sum(o => o.Total),
                orders
            };
            return Ok(profile);
        }
    }
}
