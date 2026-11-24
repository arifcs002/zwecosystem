using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ecommerce.Api.Tests;

[Collection("api")]
public class Batch11PurchaseTests(ApiFixture f)
{
    private static string U() => Guid.NewGuid().ToString("N")[..6];
    private Task<int> Stock(int id) => f.Scalar<int>("SELECT stock_quantity FROM products WHERE id=@i", ("i", id));
    private Task<decimal> Wholesale(int id) => f.Scalar<decimal>("SELECT wholesale_price FROM products WHERE id=@i", ("i", id));

    private async Task<int> NewSupplier(HttpClient c)
    {
        var r = await c.PostAsJsonAsync("/api/suppliers", new { name = "Supplier " + U(), phoneNumber = "01700000000", address = "Dhaka" });
        await Shop.Ok(r, "supplier");
        return (await Shop.Body(r)).GetProperty("id").GetInt32();
    }

    private async Task<(int PoId, int ItemId)> NewPo(HttpClient c, int supplierId, int productId, int qty = 10, decimal cost = 500)
    {
        var r = await c.PostAsJsonAsync("/api/purchases", new { supplierId, items = new[] { new { productId, quantity = qty, unitCost = cost } }, note = "t" });
        await Shop.Ok(r, "po");
        var id = (await Shop.Body(r)).GetProperty("id").GetInt32();
        var item = (await Shop.Body(await c.GetAsync($"/api/purchases/{id}"))).GetProperty("items")[0].GetProperty("id").GetInt32();
        return (id, item);
    }

    private Task<HttpResponseMessage> Receive(HttpClient c, int poId, int itemId, int qty, decimal? paid = null, string? method = null, bool updateCost = false) =>
        c.PostAsJsonAsync($"/api/purchases/{poId}/receive", new { lines = new[] { new { itemId, quantity = qty } }, paidNow = paid, method, updateCost });

    private async Task<decimal> Owed(HttpClient c, int supplierId) =>
        (await Shop.Body(await c.GetAsync($"/api/supplier-accounts/{supplierId}"))).GetProperty("balance").GetDecimal();

    [Fact]
    public async Task OrderingDoesNotTouchStock_ReceivingAddsItAndWhatIsOwed()
    {
        var a = await f.A.LoginAsync();
        var sup = await NewSupplier(a);
        var stock = await Stock(f.A.ProductId);
        var (po, item) = await NewPo(a, sup, f.A.ProductId, 10, 500);
        Assert.Equal(stock, await Stock(f.A.ProductId));                        // just an order so far
        Assert.Equal(0m, await Owed(a, sup));

        var r = await Receive(a, po, item, 10, paid: 2000);
        await Shop.Ok(r, "receive");
        var b = await Shop.Body(r);
        Assert.Equal("RECEIVED", b.GetProperty("status").GetString());
        Assert.Equal(5000m, b.GetProperty("receivedValue").GetDecimal());
        Assert.Equal(3000m, b.GetProperty("owedToSupplier").GetDecimal());     // 5000 received - 2000 paid
        Assert.Equal(stock + 10, await Stock(f.A.ProductId));

        Assert.Equal(HttpStatusCode.Conflict, (await Receive(a, po, item, 1)).StatusCode);   // can't receive twice
    }

    [Fact]
    public async Task PartialDeliveries_AreTrackedUntilEverythingArrives()
    {
        var a = await f.A.LoginAsync();
        var sup = await NewSupplier(a);
        var (po, item) = await NewPo(a, sup, f.A.ProductId, 10, 100);
        var stock = await Stock(f.A.ProductId);

        var first = await Shop.Body(await Receive(a, po, item, 4));
        Assert.Equal("PARTIAL", first.GetProperty("status").GetString());
        Assert.Equal(400m, await Owed(a, sup));
        Assert.Equal(HttpStatusCode.BadRequest, (await Receive(a, po, item, 7)).StatusCode);   // only 6 are still expected
        var second = await Shop.Body(await Receive(a, po, item, 6));
        Assert.Equal("RECEIVED", second.GetProperty("status").GetString());
        Assert.Equal(1000m, await Owed(a, sup));
        Assert.Equal(stock + 10, await Stock(f.A.ProductId));

        // a part-received order can no longer be cancelled
        var (po2, item2) = await NewPo(a, sup, f.A.ProductId, 5, 100);
        await Shop.Ok(await Receive(a, po2, item2, 1), "part");
        Assert.Equal(HttpStatusCode.Conflict, (await a.PostAsync($"/api/purchases/{po2}/cancel", null)).StatusCode);
    }

    [Fact]
    public async Task CostPrice_ChangesOnlyWhenYouAskForIt()
    {
        var a = await f.A.LoginAsync();
        var sup = await NewSupplier(a);
        var before = await Wholesale(f.A.ProductId);
        var (po, item) = await NewPo(a, sup, f.A.ProductId, 2, 777);
        await Shop.Ok(await Receive(a, po, item, 1), "no update");
        Assert.Equal(before, await Wholesale(f.A.ProductId));                   // profit reports keep using the old cost
        await Shop.Ok(await Receive(a, po, item, 1, updateCost: true), "update");
        Assert.Equal(777m, await Wholesale(f.A.ProductId));
        await f.Exec("UPDATE products SET wholesale_price=@w WHERE id=@i", ("w", before), ("i", f.A.ProductId));
    }

    [Fact]
    public async Task Validation_AndCancelling()
    {
        var a = await f.A.LoginAsync();
        var sup = await NewSupplier(a);
        Task<HttpResponseMessage> Post(object body) => a.PostAsJsonAsync("/api/purchases", body);

        Assert.Equal(HttpStatusCode.BadRequest, (await Post(new { supplierId = sup, items = Array.Empty<object>() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(new { supplierId = sup, items = new[] { new { productId = f.A.ProductId, quantity = 0, unitCost = 5 } } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(new { supplierId = sup, items = new[] { new { productId = f.A.ProductId, quantity = 1, unitCost = -5 } } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(new { supplierId = sup, items = new[] { new { productId = f.A.ProductId, quantity = 1, unitCost = 5 }, new { productId = f.A.ProductId, quantity = 1, unitCost = 5 } } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(new { supplierId = 999999, items = new[] { new { productId = f.A.ProductId, quantity = 1, unitCost = 5 } } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(new { supplierId = sup, items = new[] { new { productId = 999999, quantity = 1, unitCost = 5 } } })).StatusCode);

        var (po, item) = await NewPo(a, sup, f.A.ProductId, 3, 10);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.PostAsJsonAsync($"/api/purchases/{po}/receive", new { lines = Array.Empty<object>() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Receive(a, po, item + 9999, 1)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Receive(a, po, item, 1, paid: 999)).StatusCode);          // paid more than the value received
        Assert.Equal(HttpStatusCode.BadRequest, (await Receive(a, po, item, 1, paid: 5, method: "paypal")).StatusCode);

        await Shop.Ok(await a.PostAsync($"/api/purchases/{po}/cancel", null), "cancel");
        Assert.Equal(HttpStatusCode.Conflict, (await Receive(a, po, item, 1)).StatusCode);                        // cancelled orders can't be received
    }

    [Fact]
    public async Task EachShopSeesOnlyItsOwnSuppliersAndOrders()
    {
        var a = await f.A.LoginAsync(); var b = await f.B.LoginAsync();
        var supA = await NewSupplier(a);
        var (po, item) = await NewPo(a, supA, f.A.ProductId);

        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/purchases/{po}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Receive(b, po, item, 1)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsync($"/api/purchases/{po}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/supplier-accounts/{supA}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsJsonAsync("/api/supplier-accounts/payments", new { supplierId = supA, amount = 10, method = "cash" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await b.PostAsJsonAsync("/api/purchases", new { supplierId = supA, items = new[] { new { productId = f.B.ProductId, quantity = 1, unitCost = 5 } } })).StatusCode);   // B can't order from A's supplier
        Assert.Equal(HttpStatusCode.BadRequest, (await a.PostAsJsonAsync("/api/purchases", new { supplierId = supA, items = new[] { new { productId = f.B.ProductId, quantity = 1, unitCost = 5 } } })).StatusCode);   // nor A order B's product
        Assert.DoesNotContain((await Shop.Body(await b.GetAsync("/api/purchases"))).EnumerateArray(), x => x.GetProperty("id").GetInt32() == po);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.NewClient().GetAsync("/api/purchases")).StatusCode);
    }

    [Fact]
    public async Task ParallelReceiving_CannotCountTheSameGoodsTwice()
    {
        var a = await f.A.LoginAsync();
        var sup = await NewSupplier(a);
        var (po, item) = await NewPo(a, sup, f.A.ProductId, 2, 50);
        var stock = await Stock(f.A.ProductId);
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => Receive(a, po, item, 1))));
        Assert.Equal(2, results.Count(x => x.StatusCode == HttpStatusCode.OK));
        Assert.Equal(stock + 2, await Stock(f.A.ProductId));
        Assert.Equal(100m, await Owed(a, sup));
    }

    [Fact]
    public async Task SupplierAccount_Payments_Adjustments_AndTotals()
    {
        var a = await f.A.LoginAsync();
        var sup = await NewSupplier(a);
        var (po, item) = await NewPo(a, sup, f.A.ProductId, 4, 250);
        await Shop.Ok(await Receive(a, po, item, 4), "receive");                         // owes 1000
        var s0 = await Shop.Body(await a.GetAsync("/api/supplier-accounts/summary"));

        Assert.Equal(HttpStatusCode.BadRequest, (await a.PostAsJsonAsync("/api/supplier-accounts/payments", new { supplierId = sup, amount = 0, method = "cash" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.PostAsJsonAsync("/api/supplier-accounts/payments", new { supplierId = sup, amount = 5, method = "paypal" })).StatusCode);
        var p = await Shop.Body(await a.PostAsJsonAsync("/api/supplier-accounts/payments", new { supplierId = sup, amount = 400, method = "bkash", note = "TXN1" }));
        Assert.Equal(600m, p.GetProperty("balance").GetDecimal());

        Assert.Equal(HttpStatusCode.BadRequest, (await a.PostAsJsonAsync("/api/supplier-accounts/adjustments", new { supplierId = sup, amount = -50, note = " " })).StatusCode);
        var adj = await Shop.Body(await a.PostAsJsonAsync("/api/supplier-accounts/adjustments", new { supplierId = sup, amount = -100, note = "Discount given" }));
        Assert.Equal(500m, adj.GetProperty("balance").GetDecimal());

        var types = (await Shop.Body(await a.GetAsync($"/api/supplier-accounts/{sup}"))).GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("entryType").GetString()).ToArray();
        Assert.Equal(new[] { "ADJUST", "PAYMENT", "PURCHASE" }, types);
        var row = (await Shop.Body(await a.GetAsync("/api/supplier-accounts"))).EnumerateArray().First(x => x.GetProperty("id").GetInt32() == sup);
        Assert.Equal(500m, row.GetProperty("balance").GetDecimal());
        var s1 = await Shop.Body(await a.GetAsync("/api/supplier-accounts/summary"));
        Assert.Equal(s0.GetProperty("totalOwed").GetDecimal() - 500m, s1.GetProperty("totalOwed").GetDecimal());   // 1000 -> 500 owed
    }
}
