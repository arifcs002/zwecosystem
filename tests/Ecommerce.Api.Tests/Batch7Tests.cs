using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ecommerce.Api.Infrastructure;

namespace Ecommerce.Api.Tests;

[Collection("api")]
public class Batch7Tests(ApiFixture f)
{
    private async Task<int> CatId(Shop s) => await f.Scalar<int>("SELECT id FROM categories WHERE company_id=@c ORDER BY id LIMIT 1", ("c", s.CompanyId));

    private async Task<int> Create(Shop s, string name, string? uom, object? attrs)
    {
        var r = await s.Admin.PostAsJsonAsync("/api/products/batch", new
        {
            name, price = 500, wholesalePrice = 300, categoryId = await CatId(s),
            sizes = new[] { new { size = "M", quantity = 5 }, new { size = "L", quantity = 5 } },
            uom, attributes = attrs
        });
        await Shop.Ok(r, "create");
        return await f.Scalar<int>("SELECT id FROM products WHERE company_id=@c AND name=@n ORDER BY id LIMIT 1", ("c", s.CompanyId), ("n", $"{name} (Size M)"));
    }

    private async Task<JsonElement> Get(Shop s, int id) => await Shop.Body(await s.Admin.GetAsync($"/api/products/{id}"));

    [Fact]
    public async Task Create_StoresUomAndSpecs_OnEverySizeRow_WithCleanup()
    {
        var name = "Boot " + Guid.NewGuid().ToString("N")[..6];
        var id = await Create(f.A, name, "  pair ", new object[]
        {
            new { key = " Color ", value = "Black" }, new { key = "color", value = "dup" },   // case-insensitive duplicate
            new { key = "", value = "no key" }, new { key = "Material", value = "   " },     // blanks dropped
            new { key = "Material", value = "Leather" }
        });
        var p = await Get(f.A, id);
        Assert.Equal("pair", p.GetProperty("uom").GetString());
        var a = p.GetProperty("attributes").EnumerateArray().ToList();
        Assert.Equal(2, a.Count);
        Assert.Equal("Color", a[0].GetProperty("key").GetString());
        Assert.Equal("Black", a[0].GetProperty("value").GetString());
        Assert.Equal("Leather", a[1].GetProperty("value").GetString());
        Assert.False(p.TryGetProperty("attributesJson", out _));        // raw column never leaks

        Assert.Equal(2, await f.Scalar<int>("SELECT COUNT(*)::int FROM products WHERE company_id=@c AND name LIKE @n AND uom='pair'", ("c", f.A.CompanyId), ("n", name + "%")));
    }

    [Fact]
    public async Task Update_LeavesSpecsAlone_WhenNotSent_AndClearsWhenEmpty()
    {
        var id = await Create(f.A, "Sneaker " + Guid.NewGuid().ToString("N")[..6], "pair", new[] { new { key = "Color", value = "Red" } });
        var cur = await Get(f.A, id);
        object Body(string? uom, object? attrs) => new
        {
            name = cur.GetProperty("name").GetString(), sku = cur.GetProperty("sku").GetString(), price = 500, wholesalePrice = 300,
            stockQuantity = 5, size = "M", uom, attributes = attrs
        };

        // An older client that doesn't know about specs must not wipe them.
        await Shop.Ok(await f.A.Admin.PutAsJsonAsync($"/api/products/{id}", Body(null, null)), "update-old-client");
        var p = await Get(f.A, id);
        Assert.Equal("pair", p.GetProperty("uom").GetString());
        Assert.Equal(1, p.GetProperty("attributes").GetArrayLength());

        // Explicitly empty clears.
        await Shop.Ok(await f.A.Admin.PutAsJsonAsync($"/api/products/{id}", Body("", Array.Empty<object>())), "update-clear");
        p = await Get(f.A, id);
        Assert.Equal(JsonValueKind.Null, p.GetProperty("uom").ValueKind);
        Assert.Equal(0, p.GetProperty("attributes").GetArrayLength());
    }

    [Fact]
    public async Task Limits_AreEnforced()
    {
        Assert.Equal(20, ProductSpecs.CleanUom(new string('x', 99))!.Length);
        var many = Enumerable.Range(0, 50).Select(i => new SpecDto("k" + i, new string('v', 500)));
        var parsed = ProductSpecs.Parse(ProductSpecs.ToJson(many));
        Assert.Equal(20, parsed.Count);
        Assert.All(parsed, s => Assert.Equal(120, s.Value.Length));
        Assert.Null(ProductSpecs.ToJson(new[] { new SpecDto("", "x") }));
        Assert.Empty(ProductSpecs.Parse("{not json"));
    }

    [Fact]
    public async Task Storefront_ShowsUomAndSpecs_OnlyForThatShop_AndNeverTheRawJson()
    {
        var name = "Loafer " + Guid.NewGuid().ToString("N")[..6];
        await Create(f.A, name, "pair", new[] { new { key = "Sole", value = "Rubber" } });

        var body = await f.NewClient().GetStringAsync($"/api/storefront/{f.A.Slug}");
        Assert.DoesNotContain("attributesJson", body, StringComparison.OrdinalIgnoreCase);
        var doc = JsonDocument.Parse(body).RootElement;
        var row = doc.GetProperty("products").EnumerateArray().First(x => x.GetProperty("name").GetString()!.StartsWith(name));
        Assert.Equal("pair", row.GetProperty("uom").GetString());
        Assert.Equal("Rubber", row.GetProperty("attributes")[0].GetProperty("value").GetString());

        var other = await f.NewClient().GetStringAsync($"/api/storefront/{f.B.Slug}");
        Assert.DoesNotContain(name, other);
    }
}
