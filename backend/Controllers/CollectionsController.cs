using System.Text.RegularExpressions;
using Ecommerce.Api.Domain;
using Ecommerce.Api.Infrastructure;
using Ecommerce.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Api.Controllers
{
    // Curated product sets ("Eid Collection", "Back to school"). Members are product GROUP keys
    // ("<base name>::<categoryId>") so every size of a product joins at once.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class CollectionsController : ControllerBase
    {
        private const int MaxItems = 500;
        private readonly ApplicationDbContext _context;
        public CollectionsController(ApplicationDbContext context) { _context = context; }

        // ── Storefront ───────────────────────────────────────────────────────────
        [HttpGet("public")]
        [AllowAnonymous]
        public async Task<IActionResult> Public()
        {
            if (!_context.CompanyId.HasValue) return Ok(Array.Empty<object>());
            return Ok(await Load(onlyActive: true, slug: null));
        }

        [HttpGet("public/{slug}")]
        [AllowAnonymous]
        public async Task<IActionResult> PublicOne(string slug)
        {
            if (!_context.CompanyId.HasValue) return NotFound();
            var list = await Load(onlyActive: true, slug: slug);
            return list.Count == 0 ? NotFound() : Ok(list[0]);
        }

        // ── Admin ────────────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> All() => Ok(await Load(onlyActive: false, slug: null));

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CollectionUpsertDto dto)
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest(new { message = "Company context is required." });
            var error = Check(dto);
            if (error != null) return BadRequest(new { message = error });

            var c = new Collection { CompanyId = companyId.Value, CreatedBy = _context.CurrentUserId };
            Apply(c, dto);
            c.Slug = await UniqueSlug(Slugify(c.Name), null);
            _context.Collections.Add(c);
            await _context.SaveChangesAsync();
            await ReplaceItems(c, dto.ProductKeys);
            await Audit.LogAsync(_context, HttpContext, "collection.create", c.Name);
            return Ok(new { c.Id, c.Slug });
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] CollectionUpsertDto dto)
        {
            var c = await _context.Collections.FirstOrDefaultAsync(x => x.Id == id);
            if (c == null) return NotFound();
            var error = Check(dto);
            if (error != null) return BadRequest(new { message = error });
            Apply(c, dto);
            c.UpdatedBy = _context.CurrentUserId;
            c.UpdatedDate = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            await ReplaceItems(c, dto.ProductKeys);
            await Audit.LogAsync(_context, HttpContext, "collection.update", c.Name);
            return Ok(new { c.Id, c.Slug });
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var c = await _context.Collections.FirstOrDefaultAsync(x => x.Id == id);
            if (c == null) return NotFound();
            c.IsDeleted = 1;
            c.DeletedDate = DateTime.UtcNow;
            c.DeletedBy = _context.CurrentUserId;
            await _context.SaveChangesAsync();
            await _context.CollectionItems.Where(i => i.CollectionId == id).ExecuteDeleteAsync();
            await Audit.LogAsync(_context, HttpContext, "collection.delete", c.Name);
            return Ok(new { message = "Collection deleted." });
        }

        // ── helpers ──────────────────────────────────────────────────────────────
        private async Task<List<object>> Load(bool onlyActive, string? slug)
        {
            var q = _context.Collections.AsNoTracking().AsQueryable();
            if (onlyActive) q = q.Where(c => c.IsActive);
            if (slug != null) q = q.Where(c => c.Slug == slug);
            var cols = await q.OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync();
            var ids = cols.Select(c => c.Id).ToList();
            var items = await _context.CollectionItems.AsNoTracking()
                .Where(i => ids.Contains(i.CollectionId)).OrderBy(i => i.SortOrder).ToListAsync();
            return cols.Select(c => (object)new
            {
                c.Id, c.Name, c.Slug, c.Description, c.IsActive, c.SortOrder,
                productKeys = items.Where(i => i.CollectionId == c.Id).Select(i => i.ProductKey).ToList()
            }).ToList();
        }

        private async Task ReplaceItems(Collection c, List<string>? keys)
        {
            if (keys == null) return;   // null = leave the members alone
            var clean = keys.Select(k => (k ?? "").Trim()).Where(k => k.Length > 0 && k.Length <= 200)
                .Distinct().Take(MaxItems).ToList();
            await _context.CollectionItems.Where(i => i.CollectionId == c.Id).ExecuteDeleteAsync();
            var order = 0;
            _context.CollectionItems.AddRange(clean.Select(k => new CollectionItem
            { CompanyId = c.CompanyId, CollectionId = c.Id, ProductKey = k, SortOrder = order++ }));
            await _context.SaveChangesAsync();
        }

        private static string? Check(CollectionUpsertDto dto)
        {
            var name = (dto.Name ?? "").Trim();
            if (name.Length < 2 || name.Length > 80) return "Collection name must be 2-80 characters.";
            if ((dto.Description?.Length ?? 0) > 500) return "Description is too long.";
            if ((dto.ProductKeys?.Count ?? 0) > MaxItems) return $"A collection can hold at most {MaxItems} products.";
            return null;
        }

        private static void Apply(Collection c, CollectionUpsertDto dto)
        {
            c.Name = dto.Name.Trim();
            c.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
            c.IsActive = dto.IsActive;
            c.SortOrder = dto.SortOrder;
        }

        private static string Slugify(string name)
        {
            var s = Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
            return s.Length == 0 ? "collection" : (s.Length > 60 ? s[..60].Trim('-') : s);
        }

        private async Task<string> UniqueSlug(string baseSlug, int? ignoreId)
        {
            var slug = baseSlug;
            for (var n = 2; await _context.Collections.AnyAsync(c => c.Slug == slug && c.Id != (ignoreId ?? -1)); n++)
                slug = $"{baseSlug}-{n}";
            return slug;
        }
    }
}
