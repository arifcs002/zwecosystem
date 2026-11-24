using Ecommerce.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Api.Controllers
{
    public record OfferItemDto(int ProductId, int Quantity);
    public record OfferDto(string Name, string Type, int? ProductId, int? BuyQty, int? GetQty, int? MinQty, decimal? Percent, decimal? BundlePrice,
        DateTime? StartsAt, DateTime? EndsAt, bool IsActive, List<OfferItemDto>? Items);
    public record OfferPreviewLine(int ProductId, int Quantity, decimal? Price);
    public record OfferPreviewDto(List<OfferPreviewLine> Items);

    // Automatic offers ("buy 2 get 1 free", bundles, quantity discounts). The owner sets them up; checkout applies them
    // on the server, so a shopper can't invent one and a stale cart can't keep an old one.
    [ApiController]
    [Route("api/offers")]
    [Authorize]
    public class OffersController : ControllerBase
    {
        private static readonly string[] Types = { "BUY_GET", "BUNDLE", "QTY_PERCENT" };
        private readonly ApplicationDbContext _context;
        public OffersController(ApplicationDbContext context) { _context = context; }

        public sealed class OfferListRow { public int Id { get; set; } public string Name { get; set; } = ""; public string OfferType { get; set; } = ""; public int? ProductId { get; set; } public int BuyQty { get; set; } public int GetQty { get; set; } public int MinQty { get; set; } public decimal Percent { get; set; } public decimal BundlePrice { get; set; } public DateTime? StartsAt { get; set; } public DateTime? EndsAt { get; set; } public bool IsActive { get; set; } }
        public sealed class ItemListRow { public int OfferId { get; set; } public int ProductId { get; set; } public int Quantity { get; set; } }

        [HttpGet]
        public async Task<IActionResult> List()
        {
            var cid = _context.CompanyId;
            if (!cid.HasValue) return BadRequest(new { message = "Company context is required." });
            var rows = await _context.Database.SqlQueryRaw<OfferListRow>(
                @"SELECT id AS ""Id"", name AS ""Name"", offer_type AS ""OfferType"", product_id AS ""ProductId"", buy_qty AS ""BuyQty"", get_qty AS ""GetQty"", min_qty AS ""MinQty"", percent AS ""Percent"", bundle_price AS ""BundlePrice"", starts_at AS ""StartsAt"", ends_at AS ""EndsAt"", is_active AS ""IsActive""
                  FROM offers WHERE company_id={0} ORDER BY id DESC", cid.Value).ToListAsync();
            var items = await _context.Database.SqlQueryRaw<ItemListRow>(
                @"SELECT i.offer_id AS ""OfferId"", i.product_id AS ""ProductId"", i.quantity AS ""Quantity"" FROM offer_items i JOIN offers o ON o.id = i.offer_id WHERE o.company_id={0}", cid.Value).ToListAsync();
            var names = await _context.Products.AsNoTracking().Select(p => new { p.Id, p.Name }).ToDictionaryAsync(p => p.Id, p => p.Name);
            return Ok(rows.Select(r => new
            {
                r.Id, r.Name, r.OfferType, r.ProductId, productName = r.ProductId != null ? names.GetValueOrDefault(r.ProductId.Value) : null,
                r.BuyQty, r.GetQty, r.MinQty, r.Percent, r.BundlePrice, r.StartsAt, r.EndsAt, r.IsActive,
                items = items.Where(i => i.OfferId == r.Id).Select(i => new { i.ProductId, name = names.GetValueOrDefault(i.ProductId), i.Quantity })
            }));
        }

        private async Task<(string? Error, string? Name)> Validate(OfferDto dto)
        {
            var name = (dto.Name ?? "").Trim();
            if (name.Length == 0) return ("Give the offer a name.", null);
            if (name.Length > 80) name = name[..80];
            if (!Types.Contains(dto.Type)) return ("Choose the type of offer.", null);
            if (dto.StartsAt != null && dto.EndsAt != null && dto.EndsAt < dto.StartsAt) return ("The end date is before the start date.", null);
            var ids = new List<int>();
            if (dto.ProductId != null) ids.Add(dto.ProductId.Value);
            if (dto.Type == "BUY_GET")
            {
                if (dto.ProductId == null) return ("Choose the product.", null);
                if ((dto.BuyQty ?? 0) < 1 || (dto.GetQty ?? 0) < 1 || dto.BuyQty > 100 || dto.GetQty > 100) return ("Enter how many to buy and how many come free (1 or more).", null);
            }
            else if (dto.Type == "QTY_PERCENT")
            {
                if ((dto.MinQty ?? 0) < 2 || dto.MinQty > 1000) return ("The minimum quantity must be 2 or more.", null);
                if ((dto.Percent ?? 0) <= 0 || dto.Percent > 90) return ("The discount must be between 1% and 90%.", null);
            }
            else
            {
                var items = dto.Items ?? new();
                if (items.Count < 2) return ("A bundle needs at least two products.", null);
                if (items.Any(i => i.Quantity < 1 || i.Quantity > 100) || items.GroupBy(i => i.ProductId).Any(g => g.Count() > 1)) return ("Each product can be listed once, with a quantity of 1 or more.", null);
                if ((dto.BundlePrice ?? 0) <= 0) return ("Enter the bundle price.", null);
                ids.AddRange(items.Select(i => i.ProductId));
            }
            var found = await _context.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).Select(p => new { p.Id, p.Price }).ToListAsync();
            if (found.Count != ids.Distinct().Count()) return ("One of the products was not found.", null);
            if (dto.Type == "BUNDLE")
            {
                var regular = dto.Items!.Sum(i => i.Quantity * found.First(f => f.Id == i.ProductId).Price);
                if (dto.BundlePrice >= regular) return ($"The bundle price must be lower than the regular price (৳{regular:0.##}), otherwise it is no offer.", null);
            }
            return (null, name);
        }

        private async Task Save(int id, int companyId, OfferDto dto, string name)
        {
            await _context.Database.ExecuteSqlRawAsync(
                @"UPDATE offers SET name={0}, offer_type={1}, product_id={2}::int, buy_qty={3}, get_qty={4}, min_qty={5}, percent={6}, bundle_price={7}, starts_at={8}::timestamptz, ends_at={9}::timestamptz, is_active={10}
                  WHERE id={11} AND company_id={12}",
                name, dto.Type, dto.Type == "BUNDLE" ? null : dto.ProductId, dto.BuyQty ?? 0, dto.GetQty ?? 0, dto.MinQty ?? 0, dto.Percent ?? 0, dto.BundlePrice ?? 0, dto.StartsAt, dto.EndsAt, dto.IsActive, id, companyId);
            await _context.Database.ExecuteSqlRawAsync("DELETE FROM offer_items WHERE offer_id={0}", id);
            if (dto.Type == "BUNDLE")
                foreach (var i in dto.Items!)
                    await _context.Database.ExecuteSqlRawAsync("INSERT INTO offer_items (offer_id, product_id, quantity) VALUES ({0},{1},{2})", id, i.ProductId, i.Quantity);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] OfferDto dto)
        {
            var cid = _context.CompanyId;
            if (!cid.HasValue) return BadRequest(new { message = "Company context is required." });
            var (err, name) = await Validate(dto);
            if (err != null) return BadRequest(new { message = err });
            var id = (await _context.Database.SqlQueryRaw<int>(@"INSERT INTO offers (company_id, name, offer_type) VALUES ({0},{1},{2}) RETURNING id AS ""Value""", cid.Value, name!, dto.Type).ToListAsync()).First();
            await Save(id, cid.Value, dto, name!);
            await Audit.LogAsync(_context, HttpContext, "offer.create", $"{name} ({dto.Type})");
            return Ok(new { id });
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] OfferDto dto)
        {
            var cid = _context.CompanyId;
            if (!cid.HasValue) return BadRequest(new { message = "Company context is required." });
            if ((await _context.Database.SqlQueryRaw<int>(@"SELECT COUNT(*)::int AS ""Value"" FROM offers WHERE id={0} AND company_id={1}", id, cid.Value).ToListAsync()).First() == 0) return NotFound();
            var (err, name) = await Validate(dto);
            if (err != null) return BadRequest(new { message = err });
            await Save(id, cid.Value, dto, name!);
            await Audit.LogAsync(_context, HttpContext, "offer.update", $"{name} ({dto.Type}), active={dto.IsActive}");
            return Ok(new { id });
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var cid = _context.CompanyId;
            if (!cid.HasValue) return BadRequest(new { message = "Company context is required." });
            var n = await _context.Database.ExecuteSqlRawAsync("DELETE FROM offers WHERE id={0} AND company_id={1}", id, cid.Value);
            if (n == 0) return NotFound();
            await Audit.LogAsync(_context, HttpContext, "offer.delete", $"#{id}");
            return NoContent();
        }

        // Till preview: uses the price the cashier is charging (bargain mode) when given, else the shelf price.
        [HttpPost("preview")]
        public Task<IActionResult> Preview([FromBody] OfferPreviewDto dto) => Run(dto, true);

        // Storefront cart preview: always the shelf price, so a tampered price can't change the offer.
        [HttpPost("public/preview")]
        [AllowAnonymous]
        public Task<IActionResult> PublicPreview([FromBody] OfferPreviewDto dto) => Run(dto, false);

        private async Task<IActionResult> Run(OfferPreviewDto dto, bool allowCustomPrice)
        {
            var cid = _context.CompanyId;
            if (!cid.HasValue) return BadRequest(new { message = "Store context is required." });
            if (dto.Items == null || dto.Items.Count == 0 || dto.Items.Count > 100) return Ok(new { discount = 0m, labels = Array.Empty<string>() });
            var ids = dto.Items.Select(i => i.ProductId).Distinct().ToList();
            var prices = await _context.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Price);
            var lines = dto.Items.Where(i => i.Quantity > 0 && prices.ContainsKey(i.ProductId))
                .Select(i => new CartLine(i.ProductId, i.Quantity, allowCustomPrice && i.Price is > 0 ? i.Price.Value : prices[i.ProductId])).ToList();
            var r = OfferEngine.Compute(await OfferEngine.LoadActiveAsync(_context, cid.Value), lines);
            return Ok(new { discount = r.Discount, labels = r.Labels });
        }
    }
}
