using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ecommerce.Api.Tests;

[Collection("api")]
public class Batch10ImportTests(ApiFixture f)
{
    private static string U() => Guid.NewGuid().ToString("N")[..6];

    private Task<HttpResponseMessage> Import(HttpClient c, object[] rows, bool dryRun = false, bool createCats = false) =>
        c.PostAsJsonAsync("/api/products/import", new { dryRun, createMissingCategories = createCats, rows });

    private Task<int> CountProducts(Shop s) => f.Scalar<int>("SELECT COUNT(*)::int FROM products WHERE company_id=@c AND is_deleted=0", ("c", s.CompanyId));
    private static string[] Errors(JsonElement body, int row) => body.GetProperty("rows")[row].GetProperty("errors").EnumerateArray().Select(e => e.GetString()!).ToArray();

    [Fact]
    public async Task DryRun_ReportsWithoutCreatingAnything()
    {
        var a = await f.A.LoginAsync();
        var before = await CountProducts(f.A);
        var r = await Import(a, new object[] { new { name = "Dry " + U(), price = 500, wholesalePrice = 300, category = "shoes", quantity = 4 } }, dryRun: true);
        await Shop.Ok(r, "dry run");
        var b = await Shop.Body(r);
        Assert.True(b.GetProperty("dryRun").GetBoolean());
        Assert.Equal(1, b.GetProperty("summary").GetProperty("valid").GetInt32());
        Assert.Equal(before, await CountProducts(f.A));
    }

    [Fact]
    public async Task GoodFile_CreatesProductsWithSizes_Codes_Unit_AndKeepsAPrintedBarcode()
    {
        var a = await f.A.LoginAsync();
        var t = U();
        var barcode = "EAN" + Random.Shared.Next(100000000, 999999999);
        var r = await Import(a, new object[]
        {
            new { name = "Loafer " + t, price = 2500, wholesalePrice = 1500, category = "SHOES", sizes = "40:5; 41:3;42:0", description = "Leather", compareAtPrice = 3000, uom = "pair" },
            new { name = "Socks " + t, price = 120, wholesalePrice = 60, category = "Shoes", quantity = 30, barcode }
        });
        await Shop.Ok(r, "import");
        var b = await Shop.Body(r);
        Assert.Equal(2, b.GetProperty("imported").GetInt32());
        Assert.Equal(4, b.GetProperty("rowsCreated").GetInt32());                                         // 3 sizes + 1 plain

        Assert.Equal(3, await f.Scalar<int>("SELECT COUNT(*)::int FROM products WHERE company_id=@c AND name LIKE @n", ("c", f.A.CompanyId), ("n", $"Loafer {t} (Size %")));
        Assert.Equal(8, await f.Scalar<int>("SELECT SUM(stock_quantity)::int FROM products WHERE company_id=@c AND name LIKE @n", ("c", f.A.CompanyId), ("n", $"Loafer {t} (Size %")));
        Assert.Equal(3, await f.Scalar<int>("SELECT COUNT(*)::int FROM products WHERE company_id=@c AND name LIKE @n AND uom='pair' AND compare_at_price=3000 AND unique_code IS NOT NULL", ("c", f.A.CompanyId), ("n", $"Loafer {t} (Size %")));
        Assert.Equal(barcode, await f.Scalar<string>("SELECT barcode FROM products WHERE company_id=@c AND name=@n", ("c", f.A.CompanyId), ("n", "Socks " + t)));
        Assert.Equal(30, await f.Scalar<int>("SELECT stock_quantity FROM products WHERE company_id=@c AND name=@n", ("c", f.A.CompanyId), ("n", "Socks " + t)));

        // The printed barcode now finds the product at the POS.
        var hit = await a.GetAsync($"/api/pos/lookup?barcode={barcode}");
        Assert.Equal(HttpStatusCode.OK, hit.StatusCode);
    }

    [Fact]
    public async Task OneBadRow_StopsEverything_AndEachProblemIsExplained()
    {
        var a = await f.A.LoginAsync();
        var t = U();
        var before = await CountProducts(f.A);
        var existing = await f.Scalar<string>("SELECT regexp_replace(name, '\\s*\\(Size [^)]+\\)\\s*$', '') FROM products WHERE company_id=@c AND id=@i", ("c", f.A.CompanyId), ("i", f.A.ProductId));
        var takenBarcode = await f.Scalar<string>("SELECT barcode FROM products WHERE id=@i", ("i", f.A.ProductId));

        var rows = new object[]
        {
            new { name = "Fine " + t, price = 100, wholesalePrice = 50, category = "Shoes", quantity = 1 },              // 0 ok
            new { name = "", price = 100, wholesalePrice = 50, category = "Shoes", quantity = 1 },                       // 1 no name
            new { name = "Free " + t, price = 0, wholesalePrice = 50, category = "Shoes", quantity = 1 },               // 2 price 0
            new { name = "BadSize " + t, price = 100, wholesalePrice = 50, category = "Shoes", sizes = "40-5" },        // 3 sizes format
            new { name = "DupSize " + t, price = 100, wholesalePrice = 50, category = "Shoes", sizes = "40:1;40:2" },   // 4 duplicate size
            new { name = "NoCat " + t, price = 100, wholesalePrice = 50, category = "Nope " + t, quantity = 1 },        // 5 unknown category
            new { name = existing, price = 100, wholesalePrice = 50, category = "Shoes", quantity = 1 },                 // 6 already exists
            new { name = "Fine " + t, price = 100, wholesalePrice = 50, category = "Shoes", quantity = 1 },              // 7 twice in file
            new { name = "BC " + t, price = 100, wholesalePrice = 50, category = "Shoes", quantity = 1, barcode = "bad barcode!" },   // 8 bad barcode
            new { name = "BCs " + t, price = 100, wholesalePrice = 50, category = "Shoes", sizes = "40:1", barcode = "OK123" },       // 9 barcode + sizes
            new { name = "BCt " + t, price = 100, wholesalePrice = 50, category = "Shoes", quantity = 1, barcode = takenBarcode },    // 10 barcode taken
            new { name = "Neg " + t, price = 100, wholesalePrice = 50, category = "Shoes", quantity = -3 }                            // 11 negative stock
        };
        var r = await Import(a, rows);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var b = await Shop.Body(r);
        Assert.Equal(11, b.GetProperty("summary").GetProperty("withErrors").GetInt32());
        Assert.Empty(Errors(b, 0));
        Assert.Contains("Name", Errors(b, 1)[0]);
        Assert.Contains("price", Errors(b, 2)[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Sizes", Errors(b, 3)[0]);
        Assert.Contains("twice", Errors(b, 4)[0]);
        Assert.Contains("does not exist", Errors(b, 5)[0]);
        Assert.Contains("already exists", Errors(b, 6)[0]);
        Assert.Contains("twice", Errors(b, 7)[0]);
        Assert.Contains("Barcode", Errors(b, 8)[0]);
        Assert.Contains("without sizes", Errors(b, 9)[0]);
        Assert.Contains("already used", Errors(b, 10)[0]);
        Assert.Contains("negative", Errors(b, 11)[0], StringComparison.OrdinalIgnoreCase);

        Assert.Equal(before, await CountProducts(f.A));                                                  // even the good row was not imported
    }

    [Fact]
    public async Task MissingCategories_AreOnlyCreatedWhenAsked()
    {
        var a = await f.A.LoginAsync();
        var cat = "Bags " + U();
        var row = new { name = "Tote " + U(), price = 900, wholesalePrice = 500, category = cat, quantity = 5 };

        Assert.Equal(HttpStatusCode.BadRequest, (await Import(a, new object[] { row })).StatusCode);
        var dry = await Shop.Body(await Import(a, new object[] { row }, dryRun: true, createCats: true));
        Assert.Equal(cat, dry.GetProperty("summary").GetProperty("newCategories")[0].GetString());
        Assert.Equal(0, await f.Scalar<int>("SELECT COUNT(*)::int FROM categories WHERE company_id=@c AND name=@n", ("c", f.A.CompanyId), ("n", cat)));

        await Shop.Ok(await Import(a, new object[] { row }, createCats: true), "import with categories");
        Assert.Equal(1, await f.Scalar<int>("SELECT COUNT(*)::int FROM categories WHERE company_id=@c AND name=@n", ("c", f.A.CompanyId), ("n", cat)));
        Assert.Equal(1, await f.Scalar<int>("SELECT COUNT(*)::int FROM products p JOIN categories c ON c.id=p.category_id WHERE p.company_id=@c AND c.name=@n", ("c", f.A.CompanyId), ("n", cat)));
    }

    [Fact]
    public async Task AShopCanOnlyUseItsOwnCategories_AndProductsLandInTheRightShop()
    {
        var a = await f.A.LoginAsync(); var b = await f.B.LoginAsync();
        var bCat = "OnlyB " + U();
        await Shop.Ok(await b.PostAsJsonAsync("/api/categories", new { name = bCat, description = "x" }), "B category");

        var r = await Import(a, new object[] { new { name = "Sneaky " + U(), price = 100, wholesalePrice = 50, category = bCat, quantity = 1 } });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);                                            // A can't see B's category

        var name = "Mine " + U();
        await Shop.Ok(await Import(b, new object[] { new { name, price = 100, wholesalePrice = 50, category = bCat, quantity = 2 } }), "B import");
        Assert.Equal(0, await f.Scalar<int>("SELECT COUNT(*)::int FROM products WHERE company_id=@c AND name=@n", ("c", f.A.CompanyId), ("n", name)));
        Assert.Equal(1, await f.Scalar<int>("SELECT COUNT(*)::int FROM products WHERE company_id=@c AND name=@n", ("c", f.B.CompanyId), ("n", name)));
    }

    [Fact]
    public async Task SizeAndRowLimits_AndLogin_AreEnforced()
    {
        var a = await f.A.LoginAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await Import(a, Array.Empty<object>())).StatusCode);
        var many = Enumerable.Range(0, 1001).Select(i => (object)new { name = "Bulk " + i, price = 10, wholesalePrice = 5, category = "Shoes", quantity = 1 }).ToArray();
        Assert.Equal(HttpStatusCode.BadRequest, (await Import(a, many)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Import(f.NewClient(), new object[] { new { name = "x", price = 1, wholesalePrice = 1, category = "Shoes", quantity = 1 } })).StatusCode);
    }

    [Fact]
    public async Task ThePlanProductLimit_AppliesToImports_AndNothingIsCreated()
    {
        var a = await f.A.LoginAsync();
        var name = "T-Plan-" + U();
        var existing = await f.Scalar<int>("SELECT COUNT(*)::int FROM (SELECT DISTINCT regexp_replace(name, '\\s*\\(Size [^)]+\\)\\s*$', ''), category_id FROM products WHERE company_id=@c AND is_deleted=0) t", ("c", f.A.CompanyId));
        await f.Exec("INSERT INTO subscription_plans (name, price, billing_cycle, features, created_date, updated_date, is_deleted) VALUES (@n, 1, 'monthly', @f::jsonb, now(), now(), 0)", ("n", name), ("f", "{\"max_products\": " + (existing + 1) + "}"));
        var planId = await f.Scalar<int>("SELECT id FROM subscription_plans WHERE name=@n", ("n", name));
        var original = await f.Scalar<int>("SELECT subscription_plan_id FROM companies WHERE id=@c", ("c", f.A.CompanyId));
        await f.Exec("UPDATE companies SET subscription_plan_id=@p WHERE id=@c", ("p", planId), ("c", f.A.CompanyId));
        Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(f.Factory.Services).Remove($"plan-state:{f.A.CompanyId}");
        try
        {
            var before = await CountProducts(f.A);
            var r = await Import(a, new object[]
            {
                new { name = "Lim1 " + U(), price = 10, wholesalePrice = 5, category = "Shoes", quantity = 1 },
                new { name = "Lim2 " + U(), price = 10, wholesalePrice = 5, category = "Shoes", quantity = 1 }
            });
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
            Assert.Equal("plan_limit", (await Shop.Body(r)).GetProperty("code").GetString());
            Assert.Equal(before, await CountProducts(f.A));
        }
        finally
        {
            await f.Exec("UPDATE companies SET subscription_plan_id=@p WHERE id=@c", ("p", original), ("c", f.A.CompanyId));
            await f.Exec("DELETE FROM subscription_plans WHERE id=@p", ("p", planId));
            Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(f.Factory.Services).Remove($"plan-state:{f.A.CompanyId}");
        }
    }
}
