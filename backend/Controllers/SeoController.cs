using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ecommerce.Api.Domain;
using Ecommerce.Api.Infrastructure;
using Ecommerce.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Api.Controllers
{
    /// <summary>
    /// Search/social-crawler view of a storefront. The shop itself is a browser-rendered SPA, so Facebook,
    /// WhatsApp, Twitter and most crawlers (which don't run JavaScript) would see an empty page — no product
    /// name, image or price in a shared link. nginx hands crawler user-agents to these endpoints instead, which
    /// return small server-rendered HTML pages with Open Graph tags + JSON-LD, plus per-shop sitemap.xml and
    /// robots.txt. Humans never see them. Everything is public shop data; everything is HTML-encoded.
    /// </summary>
    [ApiController]
    [Route("api/seo")]
    [AllowAnonymous]
    public class SeoController : ControllerBase
    {
        private static readonly string[] RootDomains =
            (Environment.GetEnvironmentVariable("SEO_ROOT_DOMAINS") ?? "auleco.com,localhost")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        private static readonly string[] Reserved = { "www", "admin", "api" };
        private static readonly Regex HostPattern = new(@"^[a-z0-9.\-]{1,100}$", RegexOptions.Compiled);

        private readonly ApplicationDbContext _context;
        public SeoController(ApplicationDbContext context) { _context = context; }

        // ── GET /api/seo/page — one crawler-friendly HTML page ──────────────────
        [HttpGet("page")]
        public async Task<IActionResult> Page([FromQuery] string? host, [FromQuery] string? path, [FromQuery] string? scheme)
        {
            var ctx = await Resolve(host, scheme);
            if (ctx == null) return NotFoundText();

            path = (path ?? Request.Headers["X-Seo-Path"].ToString());
            if (string.IsNullOrWhiteSpace(path) || path.Length > 300) path = "/";
            path = path.Split('?', '#')[0];
            var seg = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

            var s = ctx.Settings;
            var shopTitle = Pick(s, "seo_title") ?? ctx.Name;
            var shopDesc = Pick(s, "seo_description") ?? Pick(s, "footer_about_text") ?? $"Shop online at {ctx.Name}. Cash on delivery across Bangladesh.";
            var shopImage = Abs(ctx.Base, Pick(s, "seo_og_image") ?? ctx.LogoUrl);

            string title = shopTitle, desc = shopDesc, type = "website", canonical = ctx.Base + "/";
            string? image = shopImage;
            string? jsonLd = null;
            var body = new StringBuilder();
            var extraMeta = new StringBuilder();

            if (seg.Length == 2 && seg[0] == "product" && int.TryParse(seg[1], out var productId))
            {
                var all = await LoadProducts(ctx.Id);
                var anchor = all.FirstOrDefault(p => p.Id == productId);
                if (anchor == null) return NotFoundText();
                var key = ProductKey.For(anchor);
                var group = all.Where(p => ProductKey.For(p) == key).OrderBy(p => p.Id).ToList();
                var inStock = group.Where(p => p.StockQuantity > 0).ToList();
                var price = (inStock.Count > 0 ? inStock : group).Min(p => p.Price);
                var name = ProductKey.BaseName(anchor.Name);
                var d = Clip(group.Select(p => p.Description).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)), 300);
                title = $"{name} — {ctx.Name}";
                desc = d ?? $"Buy {name} for ৳{price:0.##} at {ctx.Name}. Cash on delivery.";
                image = Abs(ctx.Base, group.Select(p => p.ImageUrl).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))) ?? shopImage;
                type = "product";
                canonical = $"{ctx.Base}/product/{(inStock.FirstOrDefault() ?? group[0]).Id}";
                var available = inStock.Count > 0;
                extraMeta.Append($"<meta property=\"product:price:amount\" content=\"{price:0.00}\">\n<meta property=\"product:price:currency\" content=\"BDT\">\n<meta property=\"product:availability\" content=\"{(available ? "in stock" : "out of stock")}\">\n");

                var rating = await _context.Reviews.IgnoreQueryFilters().AsNoTracking()
                    .Where(r => r.CompanyId == ctx.Id && r.ProductKey == key && r.Status == "APPROVED" && r.IsDeleted == 0)
                    .Select(r => r.Rating).ToListAsync();
                var ld = new Dictionary<string, object?>
                {
                    ["@context"] = "https://schema.org", ["@type"] = "Product", ["name"] = name, ["description"] = desc,
                    ["image"] = image == null ? null : new[] { image },
                    ["offers"] = new Dictionary<string, object?>
                    {
                        ["@type"] = "Offer", ["url"] = canonical, ["priceCurrency"] = "BDT", ["price"] = price.ToString("0.00"),
                        ["availability"] = available ? "https://schema.org/InStock" : "https://schema.org/OutOfStock"
                    }
                };
                if (rating.Count > 0)
                    ld["aggregateRating"] = new Dictionary<string, object?>
                    { ["@type"] = "AggregateRating", ["ratingValue"] = Math.Round(rating.Average(), 1), ["reviewCount"] = rating.Count };
                jsonLd = JsonSerializer.Serialize(ld);

                body.Append($"<h1>{H(name)}</h1><p>৳{price:0.##} — {(available ? "In stock" : "Out of stock")}</p>");
                if (d != null) body.Append($"<p>{H(d)}</p>");
                foreach (var v in group.Where(v => !string.IsNullOrWhiteSpace(v.Size))) body.Append($"<span>Size {H(v.Size!)} </span>");
            }
            else if (seg.Length == 2 && seg[0] == "collection")
            {
                var col = await _context.Collections.IgnoreQueryFilters().AsNoTracking()
                    .FirstOrDefaultAsync(c => c.CompanyId == ctx.Id && c.Slug == seg[1].ToLowerInvariant() && c.IsActive && c.IsDeleted == 0);
                if (col == null) return NotFoundText();
                title = $"{col.Name} — {ctx.Name}";
                desc = Clip(col.Description, 300) ?? $"{col.Name}: hand-picked products at {ctx.Name}.";
                canonical = $"{ctx.Base}/collection/{col.Slug}";
                body.Append($"<h1>{H(col.Name)}</h1>");
                if (col.Description != null) body.Append($"<p>{H(col.Description)}</p>");
            }
            else if (seg.Length == 2 && seg[0] == "category" && int.TryParse(seg[1], out var catId))
            {
                var cat = await _context.Categories.IgnoreQueryFilters().AsNoTracking()
                    .FirstOrDefaultAsync(c => c.CompanyId == ctx.Id && c.Id == catId && c.IsDeleted == 0);
                if (cat == null) return NotFoundText();
                title = $"{cat.Name} — {ctx.Name}";
                desc = $"Shop {cat.Name} at {ctx.Name}. Cash on delivery.";
                canonical = $"{ctx.Base}/category/{cat.Id}";
                body.Append($"<h1>{H(cat.Name)}</h1>");
            }
            else if (seg.Length == 1 && seg[0] == "shop")
            {
                title = $"All products — {ctx.Name}";
                canonical = ctx.Base + "/shop";
                body.Append($"<h1>{H(title)}</h1>");
            }
            else
            {
                // Home (and any other storefront URL): shop-level description + a crawlable product list.
                jsonLd = JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["@context"] = "https://schema.org", ["@type"] = "Store", ["name"] = ctx.Name, ["url"] = ctx.Base + "/",
                    ["logo"] = Abs(ctx.Base, ctx.LogoUrl), ["description"] = desc
                });
                body.Append($"<h1>{H(ctx.Name)}</h1><p>{H(desc)}</p>");
            }

            if (jsonLd == null && seg.Length == 0) { /* home handled above */ }

            // A crawlable list of products helps discovery on listing pages.
            if (type == "website")
            {
                var list = (await LoadProducts(ctx.Id)).GroupBy(ProductKey.For).Select(g => g.First()).Take(48).ToList();
                body.Append("<ul>");
                foreach (var p in list) body.Append($"<li><a href=\"{HA($"{ctx.Base}/product/{p.Id}")}\">{H(ProductKey.BaseName(p.Name))}</a> ৳{p.Price:0.##}</li>");
                body.Append("</ul>");
            }

            var html = new StringBuilder();
            html.Append("<!doctype html>\n<html lang=\"en\"><head><meta charset=\"utf-8\">\n");
            html.Append($"<title>{H(title)}</title>\n<meta name=\"description\" content=\"{HA(Clip(desc, 300) ?? "")}\">\n");
            html.Append($"<link rel=\"canonical\" href=\"{HA(canonical)}\">\n");
            html.Append($"<meta property=\"og:type\" content=\"{type}\">\n<meta property=\"og:site_name\" content=\"{HA(ctx.Name)}\">\n");
            html.Append($"<meta property=\"og:title\" content=\"{HA(title)}\">\n<meta property=\"og:description\" content=\"{HA(Clip(desc, 300) ?? "")}\">\n<meta property=\"og:url\" content=\"{HA(canonical)}\">\n");
            if (image != null) html.Append($"<meta property=\"og:image\" content=\"{HA(image)}\">\n");
            html.Append($"<meta name=\"twitter:card\" content=\"{(image != null ? "summary_large_image" : "summary")}\">\n<meta name=\"twitter:title\" content=\"{HA(title)}\">\n<meta name=\"twitter:description\" content=\"{HA(Clip(desc, 200) ?? "")}\">\n");
            if (image != null) html.Append($"<meta name=\"twitter:image\" content=\"{HA(image)}\">\n");
            html.Append(extraMeta);
            if (jsonLd != null) html.Append($"<script type=\"application/ld+json\">{jsonLd}</script>\n");
            html.Append("</head><body>").Append(body).Append("</body></html>");

            Response.Headers["Cache-Control"] = "public, max-age=300";
            return Content(html.ToString(), "text/html; charset=utf-8");
        }

        // ── GET /api/seo/sitemap — sitemap.xml for the shop ─────────────────────
        [HttpGet("sitemap")]
        public async Task<IActionResult> Sitemap([FromQuery] string? host, [FromQuery] string? scheme)
        {
            var ctx = await Resolve(host, scheme);
            if (ctx == null) return NotFoundText();

            var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n");
            void Url(string loc, DateTime? last = null) =>
                sb.Append($"<url><loc>{X(loc)}</loc>{(last.HasValue ? $"<lastmod>{last.Value:yyyy-MM-dd}</lastmod>" : "")}</url>\n");

            Url(ctx.Base + "/"); Url(ctx.Base + "/shop");
            var cols = await _context.Collections.IgnoreQueryFilters().AsNoTracking()
                .Where(c => c.CompanyId == ctx.Id && c.IsActive && c.IsDeleted == 0).OrderBy(c => c.SortOrder).ToListAsync();
            foreach (var c in cols) Url($"{ctx.Base}/collection/{c.Slug}", c.UpdatedDate);
            var cats = await _context.Categories.IgnoreQueryFilters().AsNoTracking()
                .Where(c => c.CompanyId == ctx.Id && c.IsDeleted == 0).Select(c => c.Id).ToListAsync();
            foreach (var id in cats) Url($"{ctx.Base}/category/{id}");
            foreach (var g in (await LoadProducts(ctx.Id)).GroupBy(ProductKey.For).Take(5000))
            {
                var first = g.OrderByDescending(p => p.StockQuantity > 0).ThenBy(p => p.Id).First();
                Url($"{ctx.Base}/product/{first.Id}", g.Max(p => p.UpdatedDate));
            }
            sb.Append("</urlset>");
            Response.Headers["Cache-Control"] = "public, max-age=3600";
            return Content(sb.ToString(), "application/xml; charset=utf-8");
        }

        // ── GET /api/seo/robots — robots.txt for the shop ───────────────────────
        [HttpGet("robots")]
        public async Task<IActionResult> Robots([FromQuery] string? host, [FromQuery] string? scheme)
        {
            var ctx = await Resolve(host, scheme);
            if (ctx == null) return NotFoundText();
            var txt = "User-agent: *\nAllow: /\nDisallow: /workspace\nDisallow: /admin\nDisallow: /checkout\nDisallow: /account\nDisallow: /login\nDisallow: /track-order\n"
                      + $"Sitemap: {ctx.Base}/sitemap.xml\n";
            Response.Headers["Cache-Control"] = "public, max-age=3600";
            return Content(txt, "text/plain; charset=utf-8");
        }

        // ── helpers ──────────────────────────────────────────────────────────────
        private record ShopCtx(int Id, string Name, string? LogoUrl, string Base, Dictionary<string, string> Settings);

        private async Task<ShopCtx?> Resolve(string? host, string? scheme)
        {
            host = (host ?? Request.Headers["X-Seo-Host"].ToString()).Trim().ToLowerInvariant();
            var colon = host.IndexOf(':');
            var hostNoPort = colon >= 0 ? host[..colon] : host;
            if (!HostPattern.IsMatch(hostNoPort)) return null;

            string? slug = null;
            foreach (var root in RootDomains)
                if (hostNoPort.EndsWith("." + root, StringComparison.Ordinal))
                {
                    var sub = hostNoPort[..^(root.Length + 1)];
                    if (!sub.Contains('.') && !Reserved.Contains(sub)) { slug = sub; break; }
                }
            if (slug == null) return null;

            var company = await _context.Companies.IgnoreQueryFilters().AsNoTracking()
                .Where(c => c.Subdomain == slug && c.IsDeleted == 0 && c.IsActive)
                .Select(c => new { c.Id, c.Name, c.LogoUrl }).FirstOrDefaultAsync();
            if (company == null) return null;

            scheme = (scheme ?? Request.Headers["X-Forwarded-Proto"].ToString()).ToLowerInvariant();
            if (scheme != "http" && scheme != "https") scheme = "https";
            var settings = (await _context.Database.SqlQueryRaw<SettingVm>("SELECT * FROM sp_get_settings({0})", company.Id).ToListAsync())
                .Where(s => SettingsController.IsPublicSettingKey(s.key) && !string.IsNullOrWhiteSpace(s.value))
                .GroupBy(s => s.key).ToDictionary(g => g.Key, g => g.First().value);
            return new ShopCtx(company.Id, company.Name, company.LogoUrl, $"{scheme}://{host}", settings);
        }

        private Task<List<Product>> LoadProducts(int companyId) =>
            _context.Products.IgnoreQueryFilters().AsNoTracking()
                .Where(p => p.CompanyId == companyId && p.IsDeleted == 0).OrderBy(p => p.Id).ToListAsync();

        private static string? Pick(Dictionary<string, string> s, string key) => s.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

        private static string? Abs(string baseUrl, string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return url;
            return baseUrl + (url.StartsWith('/') ? "" : "/") + url;
        }

        private static string? Clip(string? s, int max)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = Regex.Replace(s, @"\s+", " ").Trim();
            return s.Length > max ? s[..(max - 1)].TrimEnd() + "…" : s;
        }

        private static string H(string s) => WebUtility.HtmlEncode(s);
        private static string HA(string s) => WebUtility.HtmlEncode(s).Replace("\"", "&quot;");
        private static string X(string s) => System.Security.SecurityElement.Escape(s) ?? "";

        private ContentResult NotFoundText() => new() { StatusCode = 404, Content = "Not found", ContentType = "text/plain; charset=utf-8" };
    }
}
