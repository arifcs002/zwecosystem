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
    public class OrdersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IFileTextLogger _fileLogger;

        public OrdersController(ApplicationDbContext context, IFileTextLogger fileLogger)
        {
            _context = context;
            _fileLogger = fileLogger;
        }

        [HttpGet]
        public async Task<IActionResult> GetOrders()
        {
            var companyId = _context.CompanyId ?? 0;
            var orders = await _context.Database
                .SqlQueryRaw<OrderListVm>("SELECT * FROM sp_get_orders({0})", companyId)
                .ToListAsync();
            return Ok(orders);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetOrder(int id)
        {
            var order = await _context.Database
                .SqlQueryRaw<OrderListVm>("SELECT * FROM sp_get_order({0})", id)
                .ToListAsync();
            if (!order.Any()) return NotFound();
            return Ok(order.First());
        }

        [HttpPut("{id}/status")]
        public async Task<IActionResult> UpdateOrderStatus(int id, [FromBody] StatusUpdateDto dto)
        {
            if (!await _context.Orders.AnyAsync(o => o.Id == id)) return NotFound();

            await _context.Database.ExecuteSqlRawAsync(
                "CALL sp_update_order_status({0},{1},{2})", id, dto.Status, dto.Notes ?? "");

            var order = await _context.Database
                .SqlQueryRaw<OrderListVm>("SELECT * FROM sp_get_order({0})", id)
                .ToListAsync();
            return Ok(order.FirstOrDefault());
        }

        [HttpPost("{id}/cancel")]
        public async Task<IActionResult> CancelOrder(int id)
        {
            if (!await _context.Orders.AnyAsync(o => o.Id == id)) return NotFound();
            await _context.Database.ExecuteSqlRawAsync("CALL sp_cancel_order({0})", id);
            return Ok(new { message = "Order cancelled." });
        }
    }
}
