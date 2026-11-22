using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ecommerce.Api.Infrastructure;

namespace Ecommerce.Api.Tests;

[Collection("api")]
public class Batch4Tests(ApiFixture f)
{
    private string HostA => $"{f.A.Slug}.localhost";
    private string HostB => $"{f.B.Slug}.localhost";

    private Task<HttpResponseMessage> Seo(string host, string path, string kind = "page", string scheme = "https") =>
        f.NewClient().GetAsync($"/api/seo/{kind}?host={Uri.EscapeDataString(host)}&path={Uri.EscapeDataString(path)}&scheme={scheme}");

    private static async Task<string> Html(HttpResponseMessage r) => await r.Content.ReadAsStringAsync();

    // ── Crawler pages ────────────────────────────────────────────────────────

    [Fact]
    public async Task ProductPage_HasOpenGraphPriceAndJsonLd_ForTheRightShop()
    {
        var r = await Seo(HostA, $"/product/{f.A.ProductId}");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.StartsWith("text/html", r.Content.Headers.ContentType!.ToString());
        var html = await Html(r);

        Assert.Contains($"<title>Item {f.A.Slug} — Test Shop {f.A.Slug}</title>", html);
        Assert.Contains("property=\"og:type\" content=\"product\"", html);
        Assert.Contains($"property=\"og:url\" content=\"https://{HostA}/product/{f.A.ProductId}\"", html);
        Assert.Contains("property=\"product:price:amount\" content=\"1000.00\"", html);
        Assert.Contains("property=\"product:price:currency\" content=\"BDT\"", html);
        Assert.Contains("rel=\"canonical\"", html);

        var ld = Regex.Match(html, "<script type=\"application/ld\\+json\">(.*?)</script>", RegexOptions.Singleline).Groups[1].Value;
        var doc = JsonDocument.Parse(ld).RootElement;
        Assert.Equal("Product", doc.GetProperty("@type").GetString());
        Assert.Equal("1000.00", doc.GetProperty("offers").GetProperty("price").GetString());
        Assert.Equal("https://schema.org/InStock", doc.GetProperty("offers").GetProperty("availability").GetString());

        // Never leaks cost price, another shop's data, or admin-only fields.
        Assert.DoesNotContain("600", html.Replace("1000", ""));      // wholesale price is 600
        Assert.DoesNotContain(f.B.Slug, html);
    }

    [Fact]
    public async Task SeoPage_NeverTrustsOrReflectsRawInput_AndRejectsUnknownHostsAndProducts()
    {
        // Another shop's product id is not found under this shop's host.
        Assert.Equal(HttpStatusCode.NotFound, (await Seo(HostA, $"/product/{f.B.ProductId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Seo(HostA, "/product/999999999")).StatusCode);
        // Unknown shop / root domain / reserved names / malformed hosts.
        Assert.Equal(HttpStatusCode.NotFound, (await Seo("nosuchshop123.localhost", "/")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Seo("localhost", "/")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Seo("www.localhost", "/")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Seo("a.b.localhost", "/")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Seo("evil\"><script>.localhost", "/")).StatusCode);

        // A malicious product name / shop description is HTML-encoded, in tags and attributes and JSON-LD.
        var admin = await f.A.LoginAsync();
        var cats = await admin.GetFromJsonAsync<JsonElement>("/api/categories", ApiFixture.Json);
        var catId = cats.EnumerateArray().First().GetProperty("id").GetInt32();
        var evil = "Evil\"><script>alert(1)</script>";
        var created = await admin.PostAsJsonAsync("/api/products/batch", new
        {
            name = evil, price = 50, wholesalePrice = 20, categoryId = catId, description = "x\"><img src=x onerror=alert(1)>",
            sizes = new[] { new { size = "40", quantity = 3 } }
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var evilId = await f.Scalar<int>("SELECT id FROM products WHERE company_id=@c AND name LIKE 'Evil%' ORDER BY id DESC LIMIT 1", ("c", f.A.CompanyId));

        var html = await Html(await Seo(HostA, $"/product/{evilId}"));
        Assert.DoesNotContain("<script>alert(1)", html);
        Assert.DoesNotContain("<img src=x", html);
        Assert.DoesNotContain("\"><script", html);
        Assert.Contains("&lt;script&gt;", html);
        // JSON-LD must stay a single valid JSON object (a stray </script> would break out of it).
        var ld = Regex.Match(html, "<script type=\"application/ld\\+json\">(.*?)</script>", RegexOptions.Singleline).Groups[1].Value;
        Assert.Equal("Product", JsonDocument.Parse(ld).RootElement.GetProperty("@type").GetString());
    }

    [Fact]
    public async Task HomeShopAndCollectionPages_UseTheShopSeoSettings()
    {
        var admin = await f.A.LoginAsync();
        var saved = await admin.PutAsJsonAsync("/api/settings", new[]
        {
            new { key = "seo_title", value = "Best Shoes Dhaka", groupName = "SEO" },
            new { key = "seo_description", value = "Authentic shoes, cash on delivery.", groupName = "SEO" },
            new { key = "seo_og_image", value = "/uploads/og.png", groupName = "SEO" }
        });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var home = await Html(await Seo(HostA, "/"));
        Assert.Contains("<title>Best Shoes Dhaka</title>", home);
        Assert.Contains("content=\"Authentic shoes, cash on delivery.\"", home);
        Assert.Contains($"property=\"og:image\" content=\"https://{HostA}/uploads/og.png\"", home);   // relative → absolute
        Assert.Contains("\"@type\":\"Store\"", home);
        Assert.Contains($"/product/{f.A.ProductId}", home);                                           // crawlable product links

        var shop = await Html(await Seo(HostA, "/shop?q=abc"));
        Assert.Contains("All products", shop);

        // Collections: only active ones exist for crawlers.
        var created = await admin.PostAsJsonAsync("/api/collections", new { name = "Seo Col", description = "d", isActive = true, sortOrder = 1, productKeys = new string[0] });
        var slug = (await Shop.Body(created)).GetProperty("slug").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await Seo(HostA, $"/collection/{slug}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Seo(HostB, $"/collection/{slug}")).StatusCode);
    }

    [Fact]
    public async Task Sitemap_ListsOnlyThisShopsPages_AndRobotsPointsToIt()
    {
        var sitemap = await f.NewClient().GetAsync($"/api/seo/sitemap?host={HostA}&scheme=https");
        Assert.Equal(HttpStatusCode.OK, sitemap.StatusCode);
        Assert.StartsWith("application/xml", sitemap.Content.Headers.ContentType!.ToString());
        var xml = await sitemap.Content.ReadAsStringAsync();
        var doc = System.Xml.Linq.XDocument.Parse(xml);               // must be well-formed XML
        var locs = doc.Descendants().Where(e => e.Name.LocalName == "loc").Select(e => e.Value).ToList();
        Assert.Contains($"https://{HostA}/", locs);
        Assert.Contains($"https://{HostA}/shop", locs);
        Assert.Contains($"https://{HostA}/product/{f.A.ProductId}", locs);
        Assert.All(locs, l => Assert.StartsWith($"https://{HostA}", l));            // never another shop's URL
        Assert.DoesNotContain(locs, l => l.Contains($"/product/{f.B.ProductId}"));

        var robots = await (await f.NewClient().GetAsync($"/api/seo/robots?host={HostA}&scheme=https")).Content.ReadAsStringAsync();
        Assert.Contains($"Sitemap: https://{HostA}/sitemap.xml", robots);
        Assert.Contains("Disallow: /checkout", robots);
        Assert.Contains("Disallow: /workspace", robots);
        Assert.Equal(HttpStatusCode.NotFound, (await f.NewClient().GetAsync("/api/seo/sitemap?host=nosuch999.localhost")).StatusCode);
    }

    // ── Marketing settings ───────────────────────────────────────────────────

    [Fact]
    public async Task PixelIdIsPublic_ButTheCapiTokenNeverIs()
    {
        var admin = await f.A.LoginAsync();
        await admin.PutAsJsonAsync("/api/settings", new[]
        {
            new { key = "meta_pixel_id", value = "123456789012345", groupName = "MARKETING" },
            new { key = "ga4_measurement_id", value = "G-ABCD123456", groupName = "MARKETING" },
            new { key = "secret_meta_capi_token", value = "EAAB-SUPER-SECRET-TOKEN", groupName = "MARKETING" }
        });

        var pub = await f.A.Public.GetStringAsync("/api/settings/public");
        Assert.Contains("123456789012345", pub);
        Assert.Contains("G-ABCD123456", pub);
        Assert.DoesNotContain("EAAB-SUPER-SECRET-TOKEN", pub);
        Assert.DoesNotContain("secret_meta", pub);

        var storefront = await f.NewClient().GetStringAsync($"/api/storefront/{f.A.Slug}");
        Assert.DoesNotContain("EAAB-SUPER-SECRET-TOKEN", storefront);
        Assert.DoesNotContain("EAAB-SUPER-SECRET-TOKEN", await admin.GetStringAsync("/api/settings"));    // masked even for staff
        Assert.DoesNotContain("123456789012345", await f.B.Public.GetStringAsync("/api/settings/public")); // other shop never sees it
    }

    // ── Meta Conversions API ─────────────────────────────────────────────────

    private async Task<List<(string PixelId, string Token, string? TestCode, CapiPurchase Purchase)>> WaitForCapi(int expectedAtLeast)
    {
        for (var i = 0; i < 40; i++)
        {
            lock (f.Capi.Sent) if (f.Capi.Sent.Count >= expectedAtLeast) break;
            await Task.Delay(100);
        }
        lock (f.Capi.Sent) return f.Capi.Sent.ToList();
    }

    [Fact]
    public async Task Purchase_IsSentToMetaOnlyWhenConfigured_WithAMatchingEventIdAndHashedData()
    {
        // Not configured => nothing is sent.
        var before = f.Capi.Sent.Count;
        await f.B.PlaceOrder();
        await Task.Delay(400);
        Assert.Equal(before, f.Capi.Sent.Count);

        var admin = await f.A.LoginAsync();
        await admin.PutAsJsonAsync("/api/settings", new[]
        {
            new { key = "meta_pixel_id", value = "999888777666", groupName = "MARKETING" },
            new { key = "secret_meta_capi_token", value = "TOKEN-A", groupName = "MARKETING" },
            new { key = "meta_test_event_code", value = "TEST123", groupName = "MARKETING" }
        });

        var order = await f.A.Public.PostAsJsonAsync("/api/orders/public", new
        {
            items = new[] { new { productId = f.A.ProductId, quantity = 2 } }, customerName = "Buyer", customerPhone = "01712345678",
            shippingAddress = "Addr", paymentMethod = "COD", fbp = "fb.1.1596403881668.1116446470", fbc = "fb.1.1554763741205.AbCdEf",
            sourceUrl = $"https://{HostA}/checkout"
        });
        Assert.Equal(HttpStatusCode.OK, order.StatusCode);                       // checkout never waits on Meta
        var orderNo = (await Shop.Body(order)).GetProperty("orderNumber").GetString()!;

        var sent = (await WaitForCapi(before + 1)).Skip(before).ToList();
        var call = Assert.Single(sent);
        Assert.Equal("999888777666", call.PixelId);
        Assert.Equal("TOKEN-A", call.Token);
        Assert.Equal("TEST123", call.TestCode);
        var p = call.Purchase;
        Assert.Equal(orderNo, p.EventId);                                        // same id as the browser pixel => de-duplicated
        Assert.Equal(2000m, p.Value);
        Assert.Equal("BDT", p.Currency);
        Assert.Equal(f.A.ProductId, Assert.Single(p.Items).ProductId);
        Assert.Equal("fb.1.1596403881668.1116446470", p.Fbp);
        Assert.Equal($"https://{HostA}/checkout", p.SourceUrl);
        Assert.Equal("01712345678", p.Phone);                                    // raw here; hashing happens inside the real client
    }

    [Fact]
    public async Task CapiFailureOrJunkInput_NeverBreaksCheckout_AndNeverForwardsJunk()
    {
        var admin = await f.A.LoginAsync();
        await admin.PutAsJsonAsync("/api/settings", new[]
        {
            new { key = "meta_pixel_id", value = "555666777888", groupName = "MARKETING" },
            new { key = "secret_meta_capi_token", value = "TOKEN-B", groupName = "MARKETING" }
        });
        var before = f.Capi.Sent.Count;
        f.Capi.FailNext = true;
        var order = await f.A.Public.PostAsJsonAsync("/api/orders/public", new
        {
            items = new[] { new { productId = f.A.ProductId, quantity = 1 } }, customerName = "Buyer", customerPhone = "01712345678",
            shippingAddress = "Addr", paymentMethod = "COD", fbp = "<script>alert(1)</script>", fbc = new string('x', 500), sourceUrl = "javascript:alert(1)"
        });
        Assert.Equal(HttpStatusCode.OK, order.StatusCode);                       // Meta rejecting the event does not fail the order

        var sent = (await WaitForCapi(before + 1)).Skip(before).ToList();
        var p = Assert.Single(sent).Purchase;
        Assert.Null(p.Fbp);                                                      // not cookie-safe => dropped
        Assert.Null(p.Fbc);                                                      // too long => dropped
        Assert.Null(p.SourceUrl);                                                // not http(s) => dropped
    }

    [Theory]
    [InlineData("123456789012345", true)]
    [InlineData("1234", false)]
    [InlineData("12345abc", false)]
    [InlineData("1234567890123456789012", false)]
    [InlineData("'; alert(1)//", false)]
    public void PixelIdValidation_OnlyAllowsDigits(string id, bool valid) => Assert.Equal(valid, MetaCapiClient.IsValidPixelId(id));

    [Fact]
    public void CapiHashing_FollowsMetaRules()
    {
        // Meta: lowercase + trim, then SHA-256 hex. Bangladeshi numbers go in with the 880 country code.
        Assert.Equal(MetaCapiClient.Sha256("8801712345678"), MetaCapiClient.HashPhone("01712345678"));
        Assert.Equal(MetaCapiClient.HashPhone("01712345678"), MetaCapiClient.HashPhone("+880 1712-345678"));
        Assert.Null(MetaCapiClient.HashPhone("12345"));
        Assert.Equal(MetaCapiClient.Sha256("a@b.com"), MetaCapiClient.HashEmail("  A@B.com "));
        Assert.Null(MetaCapiClient.HashEmail("not-an-email"));
        Assert.Matches("^[0-9a-f]{64}$", MetaCapiClient.Sha256("x"));
    }
}
