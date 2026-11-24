using System.Text.RegularExpressions;
using Ecommerce.Api.Domain;
using Ecommerce.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace Ecommerce.Api.Controllers
{
    public class ImportRowDto
    {
        public string? Name { get; set; }
        public decimal? Price { get; set; }
        public decimal? WholesalePrice { get; set; }
        public string? Category { get; set; }
        public int? Quantity { get; set; }
        public string? Sizes { get; set; }            // "40:5; 41:3" (size:quantity pairs)
        public string? Barcode { get; set; }
        public string? Description { get; set; }
        public decimal? CompareAtPrice { get; set; }
        public string? Uom { get; set; }
    }
    public class ProductImportDto
    {
        public bool DryRun { get; set; }
        public bool CreateMissingCategories { get; set; }
        public List<ImportRowDto> Rows { get; set; } = new();
    }

    // Bulk-load products from a spreadsheet saved as CSV. The same endpoint validates (DryRun) and imports:
    // it is all-or-nothing, so one bad row can never leave a half-imported catalogue behind.
    public partial class ProductsController
    {
        public const int MaxImportRows = 1000;
        private static readonly Regex BarcodeRx = new(@"^[A-Za-z0-9._\-]{1,40}$", RegexOptions.Compiled);

        private record ParsedRow(int Index, ImportRowDto Row, string Name, string CategoryName, List<(string Size, int Qty)> Sizes, int Qty);

        [HttpPost("import")]
        public async Task<IActionResult> ImportProducts([FromBody] ProductImportDto dto)
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue) return BadRequest(new { message = "Company context is required." });
            if (dto.Rows == null || dto.Rows.Count == 0) return BadRequest(new { message = "The file has no products." });
            if (dto.Rows.Count > MaxImportRows) return BadRequest(new { message = $"Import at most {MaxImportRows} products at a time." });

            var categories = await _context.Categories.AsNoTracking().ToListAsync();
            var byName = categories.GroupBy(c => c.Name.Trim().ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First());
            var existingKeys = (await _context.Products.AsNoTracking().Select(p => new { p.Name, p.CategoryId }).ToListAsync())
                .Select(p => $"{ProductKey.BaseName(p.Name).ToLowerInvariant()}::{p.CategoryId}").ToHashSet();
            var existingBarcodes = (await _context.Products.IgnoreQueryFilters().AsNoTracking().Where(p => p.CompanyId == companyId.Value).Select(p => p.Barcode).ToListAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var existingSkus = (await _context.Products.IgnoreQueryFilters().AsNoTracking().Where(p => p.CompanyId == companyId.Value).Select(p => p.Sku).ToListAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var report = new List<object>();
            var parsed = new List<ParsedRow>();
            var newCategories = new List<string>();
            var seenKeys = new HashSet<string>();
            var seenBarcodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenSkus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var anyError = false;

            for (var i = 0; i < dto.Rows.Count; i++)
            {
                var r = dto.Rows[i] ?? new ImportRowDto();
                var errors = new List<string>(); var warnings = new List<string>();
                var name = (r.Name ?? "").Trim();
                var catName = (r.Category ?? "").Trim();

                if (name.Length == 0) errors.Add("Name is missing.");
                else if (name.Length > 150) errors.Add("Name is longer than 150 characters.");
                if (r.Price is null or <= 0 || r.Price > 10_000_000m) errors.Add("Selling price must be more than 0.");
                if (r.WholesalePrice is null or < 0 || r.WholesalePrice > 10_000_000m) errors.Add("Buying (wholesale) price is missing or invalid.");
                else if (r.Price.HasValue && r.WholesalePrice > r.Price) warnings.Add("Buying price is higher than the selling price.");
                if (r.CompareAtPrice is < 0) errors.Add("Compare-at price cannot be negative.");

                // sizes, or one plain quantity
                var sizes = new List<(string Size, int Qty)>();
                if (!string.IsNullOrWhiteSpace(r.Sizes))
                {
                    foreach (var part in r.Sizes.Split(new[] { ';', ',', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        var kv = part.Split(new[] { ':', '=' }, 2, StringSplitOptions.TrimEntries);
                        if (kv.Length != 2 || kv[0].Length == 0 || kv[0].Length > 20 || !int.TryParse(kv[1], out var q) || q < 0)
                        { errors.Add($"Sizes must look like 40:5; 41:3 — problem with “{part}”."); continue; }
                        if (sizes.Any(s => s.Size.Equals(kv[0], StringComparison.OrdinalIgnoreCase))) { errors.Add($"Size {kv[0]} is listed twice."); continue; }
                        sizes.Add((kv[0], q));
                    }
                    if (sizes.Count > 0 && sizes.All(s => s.Qty == 0)) warnings.Add("Every size has quantity 0.");
                    if (r.Quantity.HasValue) warnings.Add("Quantity is ignored because sizes are given.");
                }
                else if (r.Quantity is < 0) errors.Add("Quantity cannot be negative.");

                // category
                Category? cat = null;
                if (catName.Length == 0) errors.Add("Category is missing.");
                else if (!byName.TryGetValue(catName.ToLowerInvariant(), out cat))
                {
                    if (dto.CreateMissingCategories)
                    {
                        if (!newCategories.Contains(catName, StringComparer.OrdinalIgnoreCase)) newCategories.Add(catName);
                        warnings.Add($"Category “{catName}” will be created.");
                    }
                    else errors.Add($"Category “{catName}” does not exist (check the spelling, or tick “create missing categories”).");
                }

                // duplicates: against the shop and within the file
                var key = $"{name.ToLowerInvariant()}::{(cat?.Id.ToString() ?? "new:" + catName.ToLowerInvariant())}";
                if (name.Length > 0 && catName.Length > 0)
                {
                    if (cat != null && existingKeys.Contains($"{name.ToLowerInvariant()}::{cat.Id}")) errors.Add("This product already exists in that category. Add stock to it instead.");
                    else if (!seenKeys.Add(key)) errors.Add("This product appears twice in the file.");
                    foreach (var sku in SkusFor(name, sizes))
                    {
                        if (existingSkus.Contains(sku)) { errors.Add("A product with this name already exists (in another category)."); break; }
                        if (!seenSkus.Add(sku)) { errors.Add("This product name clashes with another row."); break; }
                    }
                }

                // barcode: only for a single (size-less) item; must be unique
                var barcode = (r.Barcode ?? "").Trim();
                if (barcode.Length > 0)
                {
                    if (sizes.Count > 0) errors.Add("A barcode can only be given for products without sizes.");
                    else if (!BarcodeRx.IsMatch(barcode)) errors.Add("Barcode may only use letters, numbers, . _ - (max 40).");
                    else if (existingBarcodes.Contains(barcode) || !seenBarcodes.Add(barcode)) errors.Add("This barcode is already used by another product.");
                }

                if (errors.Count > 0) anyError = true;
                report.Add(new { row = i + 1, name, errors, warnings });
                if (errors.Count == 0)
                    parsed.Add(new ParsedRow(i, r, name, catName, sizes, sizes.Count > 0 ? 0 : (r.Quantity ?? 0)));
            }

            if (!anyError)
            {
                var limitErr = await ProductLimitErrorAsync(companyId.Value, parsed.Select(p => (p.Name, byName.TryGetValue(p.CategoryName.ToLowerInvariant(), out var c) ? (int?)c.Id : null)));
                if (limitErr != null) return limitErr;
            }

            var summary = new { total = dto.Rows.Count, valid = parsed.Count, withErrors = dto.Rows.Count - parsed.Count, newCategories };
            if (anyError) return BadRequest(new { message = $"{summary.withErrors} row(s) need fixing. Nothing was imported.", summary, rows = report });
            if (dto.DryRun) return Ok(new { dryRun = true, summary, rows = report });

            var (fmt, pfx, pad) = await GetIdFormatAsync(companyId.Value);
            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                foreach (var name in newCategories)
                {
                    var id = (await _context.Database.SqlQueryRaw<int>("SELECT sp_create_category({0},{1},{2},{3},{4},{5},{6})",
                        companyId.Value, name, name.ToLowerInvariant().Replace(" ", "-").Replace("/", "-"), "", "", _context.CurrentUserId, (object?)DBNull.Value).ToListAsync()).First();
                    byName[name.ToLowerInvariant()] = new Category { Id = id, CompanyId = companyId.Value, Name = name };
                }

                var created = 0;
                foreach (var p in parsed)
                {
                    var category = byName[p.CategoryName.ToLowerInvariant()];
                    var slugBase = p.Name.ToLower().Replace(" ", "-").Replace("/", "-");
                    var variants = p.Sizes.Count > 0 ? p.Sizes : new List<(string Size, int Qty)> { ("", p.Qty) };
                    foreach (var (size, qty) in variants)
                    {
                        var hasSize = size.Length > 0;
                        var rowName = hasSize ? $"{p.Name} (Size {size})" : p.Name;
                        var sku = hasSize ? $"{p.Name.Replace(" ", "-").ToUpper()}-{size.ToUpper()}" : p.Name.Replace(" ", "-").ToUpper();
                        var slug = hasSize ? $"{slugBase}-{size.ToLower()}" : slugBase;
                        var barcode = GenerateBarcode(companyId.Value);
                        var productId = (await _context.Database.SqlQueryRaw<int>(
                            "SELECT sp_create_product({0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17})",
                            companyId.Value, rowName, slug, sku, barcode, p.Row.Description?.Trim() ?? "", p.Row.Price!.Value, p.Row.WholesalePrice!.Value, qty,
                            "", category.Id, (int?)null, "PUBLISHED", size, (int?)null, _context.CurrentUserId, (int?)null, p.Row.CompareAtPrice ?? 0m).ToListAsync()).FirstOrDefault();
                        await AssignCodeAsync(productId, companyId.Value, fmt, pfx, pad);
                        var custom = (p.Row.Barcode ?? "").Trim();
                        if (!hasSize && custom.Length > 0)      // keep the barcode already printed on the stock
                            await _context.Database.ExecuteSqlRawAsync("UPDATE products SET barcode = {0} WHERE id = {1}", custom, productId);
                        await ApplySpecsAsync(productId, p.Row.Uom, null);
                        created++;
                    }
                }
                await tx.CommitAsync();
                if (newCategories.Count > 0)
                    try { await HttpContext.RequestServices.GetRequiredService<IDistributedCache>().RemoveAsync($"categories:{companyId.Value}"); } catch { /* cache is best-effort */ }
                await Audit.LogAsync(_context, HttpContext, "products.import", $"{parsed.Count} products ({created} rows), {newCategories.Count} new categories");
                return Ok(new { dryRun = false, imported = parsed.Count, rowsCreated = created, summary, rows = report });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                _fileLogger.LogError("PRODUCTS", "Import failed", ex);
                return StatusCode(500, new { message = "The import failed and nothing was saved." });
            }
        }

        private static IEnumerable<string> SkusFor(string name, List<(string Size, int Qty)> sizes)
        {
            var b = name.Replace(" ", "-").ToUpper();
            return sizes.Count == 0 ? new[] { b } : sizes.Select(s => $"{b}-{s.Size.ToUpper()}");
        }
    }
}
