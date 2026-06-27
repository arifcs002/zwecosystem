using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ecommerce.Api.Domain;
using Ecommerce.Api.Infrastructure;
using Ecommerce.Api.Models;
using Microsoft.AspNetCore.Authorization;

namespace Ecommerce.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class SuppliersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IFileTextLogger _fileLogger;

        public SuppliersController(ApplicationDbContext context, IFileTextLogger fileLogger)
        {
            _context = context;
            _fileLogger = fileLogger;
        }

        [HttpGet]
        public async Task<IActionResult> GetSuppliers()
        {
            var companyId = _context.CompanyId ?? 0;
            var suppliers = await _context.Database
                .SqlQueryRaw<SupplierListVm>("SELECT * FROM sp_get_suppliers({0})", companyId)
                .ToListAsync();
            return Ok(suppliers);
        }

        [HttpPost]
        public async Task<IActionResult> CreateSupplier([FromBody] Supplier supplier)
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest("Company context is required.");

            var supplierId = (await _context.Database.SqlQueryRaw<int>(
                "SELECT sp_create_supplier({0},{1},{2},{3},{4})",
                companyId.Value, supplier.Name, supplier.PhoneNumber ?? "",
                supplier.Address ?? "", _context.CurrentUserId
            ).ToListAsync()).FirstOrDefault();

            var created = await _context.Suppliers.FindAsync(supplierId);
            return Ok(created);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteSupplier(int id)
        {
            if (!await _context.Suppliers.AnyAsync(s => s.Id == id)) return NotFound();
            await _context.Database.ExecuteSqlRawAsync("CALL sp_delete_supplier({0},{1})", id, _context.CurrentUserId);
            return NoContent();
        }
    }
}
