using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ecommerce.Api.Tests;

[Collection("api")]
public class Batch11StockCountTests(ApiFixture f)
{
    private static string U() => Guid.NewGuid().ToString("N")[..6];
    private Task<int> Stock(int id) => f.Scalar<int>("SELECT stock_quantity FROM products WHERE id=@i", ("i", id));

    /// <summary>A fresh category with two products of 10 and 20 units, so a count of just that category is exact.</summary>
    private async Task<(int Cat, int P1, int P2, string Code1)> Fixture(HttpClient c, Shop s)
    {
        var cat = await Shop.Body(await c.PostAsJsonAsync("/api/categories", new { name = "Count " + U(), description = "t" }));
        var catId = cat.GetProperty("id").GetInt32();
        var name = "Item " + U();
        await Shop.Ok(await c.PostAsJsonAsync("/api/products/batch", new { name, price = 100, wholesalePrice = 40, categoryId = catId, sizes = new[] { new { size = "A", quantity = 10 }, new { size = "B", quantity = 20 } } }), "products");
        var p1 = await f.Scalar<int>("SELECT id FROM products WHERE company_id=@c AND name=@n", ("c", s.CompanyId), ("n", $"{name} (Size A)"));
        var p2 = await f.Scalar<int>("SELECT id FROM products WHERE company_id=@c AND name=@n", ("c", s.CompanyId), ("n", $"{name} (Size B)"));
        var code1 = await f.Scalar<string>("SELECT barcode FROM products WHERE id=@i", ("i", p1));
        return (catId, p1, p2, code1);
    }

    private async Task<int> Start(HttpClient c, int cat)
    {
        var r = await c.PostAsJsonAsync("/api/stockcounts", new { name = "T " + U(), categoryId = cat });
        await Shop.Ok(r, "start count");
        return (await Shop.Body(r)).GetProperty("id").GetInt32();
    }

    private Task<HttpResponseMessage> Counted(HttpClient c, int id, params (int Product, int? Qty)[] lines) =>
        c.PutAsJsonAsync($"/api/stockcounts/{id}/lines", new { lines = lines.Select(l => new { productId = l.Product, countedQty = l.Qty }).ToArray() });

    [Fact]
    public async Task ShortagesAndSurpluses_AreValued_AndOnlyCountedItemsAreCorrected()
    {
        var a = await f.A.LoginAsync();
        var (cat, p1, p2, _) = await Fixture(a, f.A);
        var id = await Start(a, cat);

        var fresh = await Shop.Body(await a.GetAsync($"/api/stockcounts/{id}"));
        Assert.Equal(2, fresh.GetProperty("summary").GetProperty("lines").GetInt32());
        Assert.Equal(0, fresh.GetProperty("summary").GetProperty("counted").GetInt32());

        await Shop.Ok(await Counted(a, id, (p1, 8)), "count one product");                   // 2 short; the other is left uncounted
        var d = await Shop.Body(await a.GetAsync($"/api/stockcounts/{id}"));
        var s = d.GetProperty("summary");
        Assert.Equal(1, s.GetProperty("counted").GetInt32());
        Assert.Equal(1, s.GetProperty("notCounted").GetInt32());
        Assert.Equal(1, s.GetProperty("shortages").GetInt32());
        Assert.Equal(80m, s.GetProperty("shortageValue").GetDecimal());                       // 2 x wholesale 40

        var applied = await Shop.Body(await a.PostAsync($"/api/stockcounts/{id}/apply", null));
        Assert.Equal(1, applied.GetProperty("adjusted").GetInt32());
        Assert.Equal(1, applied.GetProperty("notCounted").GetInt32());
        Assert.Equal(8, await Stock(p1));
        Assert.Equal(20, await Stock(p2));                                                    // never counted, never touched
        Assert.Equal(-2, await f.Scalar<int>("SELECT quantity FROM inventory_movements WHERE company_id=@c AND product_id=@p AND movement_type='COUNT'", ("c", f.A.CompanyId), ("p", p1)));
    }

    [Fact]
    public async Task ASaleMadeDuringTheCount_IsNotWipedOut()
    {
        var a = await f.A.LoginAsync();
        var (cat, p1, _, _) = await Fixture(a, f.A);
        var id = await Start(a, cat);                                                          // system expects 10
        await Shop.Ok(await a.PostAsJsonAsync("/api/pos/checkout", new { items = new[] { new { productId = p1, quantity = 3, price = (decimal?)null } }, discount = 0, paymentMethod = "CASH" }), "sale during count");
        Assert.Equal(7, await Stock(p1));

        await Counted(a, id, (p1, 10));                                                        // the shelf still showed 10 when it was counted
        var d = await Shop.Body(await a.GetAsync($"/api/stockcounts/{id}"));
        Assert.Equal(7, d.GetProperty("lines").EnumerateArray().First(l => l.GetProperty("productId").GetInt32() == p1).GetProperty("currentStock").GetInt32());

        await Shop.Ok(await a.PostAsync($"/api/stockcounts/{id}/apply", null), "apply");
        // counted == expected, so nothing to correct: the 3 sold stay sold
        Assert.Equal(7, await Stock(p1));

        var id2 = await Start(a, cat);                                                         // expects 7 now
        await a.PostAsJsonAsync("/api/pos/checkout", new { items = new[] { new { productId = p1, quantity = 2, price = (decimal?)null } }, discount = 0, paymentMethod = "CASH" });
        await Counted(a, id2, (p1, 6));                                                        // counted 6 where 7 was expected (1 short), then 2 were sold meanwhile
        await Shop.Ok(await a.PostAsync($"/api/stockcounts/{id2}/apply", null), "apply 2");
        Assert.Equal(4, await Stock(p1));                                                      // 7 - 2 sold - 1 missing
    }

    [Fact]
    public async Task Scanning_AddsUp_AndRejectsStrangers()
    {
        var a = await f.A.LoginAsync(); var b = await f.B.LoginAsync();
        var (cat, p1, p2, code1) = await Fixture(a, f.A);
        var id = await Start(a, cat);

        for (var i = 0; i < 3; i++) await Shop.Ok(await a.PostAsJsonAsync($"/api/stockcounts/{id}/scan", new { code = code1 }), "scan");
        var two = await Shop.Body(await a.PostAsJsonAsync($"/api/stockcounts/{id}/scan", new { code = code1, quantity = 4 }));
        Assert.Equal(7, two.GetProperty("counted").GetInt32());
        Assert.Equal(10, two.GetProperty("expected").GetInt32());

        Assert.Equal(HttpStatusCode.NotFound, (await a.PostAsJsonAsync($"/api/stockcounts/{id}/scan", new { code = "NOPE-123" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.PostAsJsonAsync($"/api/stockcounts/{id}/scan", new { code = "" })).StatusCode);
        var other = await f.Scalar<string>("SELECT barcode FROM products WHERE company_id=@c AND id=@i", ("c", f.A.CompanyId), ("i", f.A.ProductId));
        Assert.Equal(HttpStatusCode.NotFound, (await a.PostAsJsonAsync($"/api/stockcounts/{id}/scan", new { code = other })).StatusCode);   // exists, but not in this count
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsJsonAsync($"/api/stockcounts/{id}/scan", new { code = code1 })).StatusCode);      // another shop's count
    }

    [Fact]
    public async Task AppliedOrCancelledCounts_AreFinal_AndParallelApplyingCorrectsOnce()
    {
        var a = await f.A.LoginAsync();
        var (cat, p1, _, _) = await Fixture(a, f.A);
        var id = await Start(a, cat);
        await Counted(a, id, (p1, 4));                                                          // 6 short
        var results = await Task.WhenAll(Enumerable.Range(0, 30).Select(_ => Task.Run(() => a.PostAsync($"/api/stockcounts/{id}/apply", null))));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(29, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(4, await Stock(p1));                                                       // corrected once: not 10 - 6 - 6 - ...
        Assert.Equal(HttpStatusCode.Conflict, (await Counted(a, id, (p1, 9))).StatusCode);       // can't edit afterwards
        Assert.Equal(HttpStatusCode.Conflict, (await a.PostAsync($"/api/stockcounts/{id}/cancel", null)).StatusCode);

        var (cat2, p3, _, _) = await Fixture(a, f.A);
        var id2 = await Start(a, cat2);
        await Counted(a, id2, (p3, 1));
        await Shop.Ok(await a.PostAsync($"/api/stockcounts/{id2}/cancel", null), "cancel");
        Assert.Equal(HttpStatusCode.Conflict, (await a.PostAsync($"/api/stockcounts/{id2}/apply", null)).StatusCode);
        Assert.Equal(10, await Stock(p3));                                                      // a cancelled count changes nothing
    }

    [Fact]
    public async Task Validation_AndTenantIsolation()
    {
        var a = await f.A.LoginAsync(); var b = await f.B.LoginAsync();
        var (cat, p1, _, _) = await Fixture(a, f.A);
        var id = await Start(a, cat);

        Assert.Equal(HttpStatusCode.BadRequest, (await Counted(a, id, (p1, -1))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Counted(a, id, (f.A.ProductId, 5))).StatusCode);   // not part of this count
        Assert.Equal(HttpStatusCode.BadRequest, (await a.PutAsJsonAsync($"/api/stockcounts/{id}/lines", new { lines = Array.Empty<object>() })).StatusCode);
        var emptyCat = (await Shop.Body(await a.PostAsJsonAsync("/api/categories", new { name = "Empty " + U(), description = "t" }))).GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.BadRequest, (await a.PostAsJsonAsync("/api/stockcounts", new { categoryId = emptyCat })).StatusCode);

        await Shop.Ok(await Counted(a, id, (p1, 5)), "ok");
        await Shop.Ok(await Counted(a, id, (p1, null)), "clear");                                       // null puts it back to "not counted"
        Assert.Equal(0, (await Shop.Body(await a.GetAsync($"/api/stockcounts/{id}"))).GetProperty("summary").GetProperty("counted").GetInt32());

        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/stockcounts/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsync($"/api/stockcounts/{id}/apply", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Counted(b, id, (p1, 1))).StatusCode);
        Assert.DoesNotContain((await Shop.Body(await b.GetAsync("/api/stockcounts"))).EnumerateArray(), x => x.GetProperty("id").GetInt32() == id);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.NewClient().GetAsync("/api/stockcounts")).StatusCode);

        await a.PostAsync($"/api/stockcounts/{id}/cancel", null);                                         // tidy: only 3 counts may be open at once
    }

    [Fact]
    public async Task OnlyThreeCountsCanBeOpenAtOnce()
    {
        var a = await f.A.LoginAsync();
        var open = await f.Scalar<int>("SELECT COUNT(*)::int FROM stock_counts WHERE company_id=@c AND status='OPEN'", ("c", f.A.CompanyId));
        var (cat, _, _, _) = await Fixture(a, f.A);
        var started = new List<int>();
        try
        {
            for (var i = open; i < 3; i++) started.Add(await Start(a, cat));
            Assert.Equal(HttpStatusCode.BadRequest, (await a.PostAsJsonAsync("/api/stockcounts", new { categoryId = cat })).StatusCode);
        }
        finally { foreach (var id in started) await a.PostAsync($"/api/stockcounts/{id}/cancel", null); }
    }
}
