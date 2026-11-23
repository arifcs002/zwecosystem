using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ecommerce.Api.Tests;

[Collection("api")]
public class Batch9PosTests(ApiFixture f)
{
    private static string Ref() => "t" + Guid.NewGuid().ToString("N");

    private Task<HttpResponseMessage> Sell(HttpClient c, int productId, int qty, string? clientRef, bool offline = false, DateTime? soldAt = null) =>
        c.PostAsJsonAsync("/api/pos/checkout", new
        {
            items = new[] { new { productId, quantity = qty, price = (decimal?)null } },
            discount = 0, paymentMethod = "CASH", clientRef, offline, soldAt
        });

    private Task<int> Stock(int id) => f.Scalar<int>("SELECT stock_quantity FROM products WHERE id=@i", ("i", id));
    private Task<int> OrdersWithRef(Shop s, string r) => f.Scalar<int>("SELECT COUNT(*)::int FROM orders WHERE company_id=@c AND client_ref=@r", ("c", s.CompanyId), ("r", r));

    [Fact]
    public async Task RetryingTheSameSale_ReturnsTheFirstResult_AndChargesOnce()
    {
        var admin = await f.A.LoginAsync();
        var r = Ref();
        var before = await Stock(f.A.ProductId);

        var first = await Sell(admin, f.A.ProductId, 2, r);
        await Shop.Ok(first, "first");
        var a = await Shop.Body(first);
        Assert.False(a.TryGetProperty("duplicate", out _));

        var second = await Sell(admin, f.A.ProductId, 2, r);                       // e.g. the response was lost and the app retried
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var b = await Shop.Body(second);
        Assert.True(b.GetProperty("duplicate").GetBoolean());
        Assert.Equal(a.GetProperty("orderNumber").GetString(), b.GetProperty("orderNumber").GetString());
        Assert.Equal(a.GetProperty("receipt").GetProperty("total").GetDecimal(), b.GetProperty("receipt").GetProperty("total").GetDecimal());

        Assert.Equal(before - 2, await Stock(f.A.ProductId));                       // stock went down once, not twice
        Assert.Equal(1, await OrdersWithRef(f.A, r));
    }

    [Fact]
    public async Task ManyParallelRetries_StillMakeExactlyOneSale()
    {
        var admin = await f.A.LoginAsync();
        var r = Ref();
        var before = await Stock(f.A.ProductId);

        var results = await Task.WhenAll(Enumerable.Range(0, 40).Select(_ => Task.Run(() => Sell(admin, f.A.ProductId, 1, r))));
        Assert.All(results, x => Assert.Equal(HttpStatusCode.OK, x.StatusCode));
        Assert.Equal(1, await OrdersWithRef(f.A, r));
        Assert.Equal(before - 1, await Stock(f.A.ProductId));
    }

    [Fact]
    public async Task SameReferenceInAnotherShop_IsADifferentSale()
    {
        var a = await f.A.LoginAsync(); var b = await f.B.LoginAsync();
        var r = Ref();
        await Shop.Ok(await Sell(a, f.A.ProductId, 1, r), "A");
        var inB = await Sell(b, f.B.ProductId, 1, r);
        await Shop.Ok(inB, "B");
        Assert.False((await Shop.Body(inB)).TryGetProperty("duplicate", out _));
        Assert.Equal(1, await OrdersWithRef(f.A, r));
        Assert.Equal(1, await OrdersWithRef(f.B, r));

        // Leave shop B with no revenue today (another test asserts that), as the other tests expect it.
        await f.Exec("UPDATE orders SET status='CANCELLED' WHERE company_id=@c AND client_ref=@r", ("c", f.B.CompanyId), ("r", r));
    }

    [Fact]
    public async Task OfflineSales_AreAcceptedEvenWhenStockRanOut_OnlineOnesAreNot()
    {
        var admin = await f.A.LoginAsync();
        await f.Exec("UPDATE products SET stock_quantity=1 WHERE id=@i", ("i", f.A.ProductId));
        try
        {
            var online = await Sell(admin, f.A.ProductId, 3, Ref());
            Assert.Equal(HttpStatusCode.BadRequest, online.StatusCode);              // normal rule still holds

            var offline = await Sell(admin, f.A.ProductId, 3, Ref(), offline: true); // the customer already walked out with it
            await Shop.Ok(offline, "offline");
            Assert.Equal(-2, await Stock(f.A.ProductId));                            // shows as a shortage to fix, not a lost sale
        }
        finally { await f.Exec("UPDATE products SET stock_quantity=500 WHERE id=@i", ("i", f.A.ProductId)); }
    }

    [Fact]
    public async Task SaleIsReportedOnTheDayItHappened_ClampedToThePast30Days()
    {
        var admin = await f.A.LoginAsync();
        var r1 = Ref(); var r2 = Ref(); var r3 = Ref();
        await Shop.Ok(await Sell(admin, f.A.ProductId, 1, r1, offline: true, soldAt: DateTime.UtcNow.AddHours(-5)), "recent");
        await Shop.Ok(await Sell(admin, f.A.ProductId, 1, r2, offline: true, soldAt: DateTime.UtcNow.AddDays(-400)), "old");
        await Shop.Ok(await Sell(admin, f.A.ProductId, 1, r3, offline: true, soldAt: DateTime.UtcNow.AddDays(5)), "future");

        async Task<double> HoursAgo(string r) => (double)await f.Scalar<decimal>("SELECT EXTRACT(EPOCH FROM (now() - created_date))/3600.0 FROM orders WHERE company_id=@c AND client_ref=@r", ("c", f.A.CompanyId), ("r", r));
        Assert.InRange(await HoursAgo(r1), 4.9, 5.2);
        Assert.InRange(await HoursAgo(r2), 29 * 24, 30.1 * 24);                       // not backdated beyond 30 days
        Assert.InRange(await HoursAgo(r3), -0.01, 0.2);                               // never in the future
    }

    [Theory]
    [InlineData("short")]
    [InlineData("has spaces in it 123")]
    [InlineData("semi;colon;injection--")]
    public async Task BadReferences_AreRejected(string bad)
    {
        var admin = await f.A.LoginAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await Sell(admin, f.A.ProductId, 1, bad)).StatusCode);
    }

    [Fact]
    public async Task SalesWithoutAReference_StillWork_ExactlyAsBefore()
    {
        var admin = await f.A.LoginAsync();
        var a = await Sell(admin, f.A.ProductId, 1, null);
        var b = await Sell(admin, f.A.ProductId, 1, null);
        await Shop.Ok(a, "a"); await Shop.Ok(b, "b");
        Assert.NotEqual((await Shop.Body(a)).GetProperty("orderNumber").GetString(), (await Shop.Body(b)).GetProperty("orderNumber").GetString());
    }

    [Fact]
    public async Task Catalog_IsTenantScoped_AndNeverCarriesCostPrices()
    {
        var admin = await f.A.LoginAsync();
        var raw = await admin.GetStringAsync("/api/pos/catalog");
        Assert.DoesNotContain("wholesale", raw, StringComparison.OrdinalIgnoreCase);
        var items = JsonDocument.Parse(raw).RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(items, i => i.GetProperty("id").GetInt32() == f.A.ProductId);
        Assert.DoesNotContain(items, i => i.GetProperty("id").GetInt32() == f.B.ProductId);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.NewClient().GetAsync("/api/pos/catalog")).StatusCode);
    }
}
