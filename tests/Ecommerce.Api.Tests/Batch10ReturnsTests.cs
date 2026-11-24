using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ecommerce.Api.Tests;

[Collection("api")]
public class Batch10ReturnsTests(ApiFixture f)
{
    // Product price is 1000, wholesale 600 (see the fixture).
    private async Task<(int OrderId, int ItemId)> Sale(HttpClient c, int qty, decimal discount = 0, string method = "CASH", string? phone = null, string? name = null, decimal? paid = null)
    {
        var r = await c.PostAsJsonAsync("/api/pos/checkout", new
        {
            items = new[] { new { productId = f.A.ProductId, quantity = qty, price = (decimal?)null } },
            discount, paymentMethod = method, customerPhone = phone, customerName = name, amountPaid = paid
        });
        await Shop.Ok(r, "sale");
        var orderId = (await Shop.Body(r)).GetProperty("orderId").GetInt32();
        var info = await Shop.Body(await c.GetAsync($"/api/returns/order/{orderId}"));
        return (orderId, info.GetProperty("items")[0].GetProperty("orderItemId").GetInt32());
    }

    private Task<HttpResponseMessage> Return(HttpClient c, int orderId, int itemId, int qty, string method = "CASH", bool restock = true, object? exchange = null, string? reason = null) =>
        c.PostAsJsonAsync("/api/returns", new { orderId, items = new[] { new { orderItemId = itemId, quantity = qty, restock } }, refundMethod = method, reason, exchangeItems = exchange });

    private Task<int> Stock(int id) => f.Scalar<int>("SELECT stock_quantity FROM products WHERE id=@i", ("i", id));
    private static string Phone() => "017" + Random.Shared.Next(10000000, 99999999);

    [Fact]
    public async Task ReturningOneOfThree_RefundsItsPrice_PutsItBackInStock_AndLimitsFurtherReturns()
    {
        var admin = await f.A.LoginAsync();
        var (orderId, itemId) = await Sale(admin, 3);
        var stock = await Stock(f.A.ProductId);

        var r = await Return(admin, orderId, itemId, 1, reason: "Wrong size");
        await Shop.Ok(r, "return");
        var b = await Shop.Body(r);
        Assert.Equal(1000m, b.GetProperty("refundAmount").GetDecimal());
        Assert.Equal(1000m, b.GetProperty("cashToGiveBack").GetDecimal());
        Assert.Equal(stock + 1, await Stock(f.A.ProductId));

        Assert.Equal(HttpStatusCode.BadRequest, (await Return(admin, orderId, itemId, 3)).StatusCode);   // only 2 left to return
        await Shop.Ok(await Return(admin, orderId, itemId, 2), "return the rest");
        var again = await Return(admin, orderId, itemId, 1);
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Contains("already", (await Shop.Body(again)).GetProperty("message").GetString());
        Assert.Equal(stock + 3, await Stock(f.A.ProductId));                                              // exactly 3 back, never more
    }

    [Fact]
    public async Task AnOrderWideDiscount_IsSharedOutSoTheRefundIsWhatWasReallyPaid()
    {
        var admin = await f.A.LoginAsync();
        var (orderId, itemId) = await Sale(admin, 3, discount: 300);                 // 3000 - 300 = 2700 paid → 900 each
        var info = await Shop.Body(await admin.GetAsync($"/api/returns/order/{orderId}"));
        Assert.Equal(900m, info.GetProperty("items")[0].GetProperty("refundPerUnit").GetDecimal());
        var b = await Shop.Body(await Return(admin, orderId, itemId, 2));
        Assert.Equal(1800m, b.GetProperty("refundAmount").GetDecimal());
    }

    [Fact]
    public async Task NotResellable_ReturnsMoneyButLeavesStockAlone()
    {
        var admin = await f.A.LoginAsync();
        var (orderId, itemId) = await Sale(admin, 1);
        var stock = await Stock(f.A.ProductId);
        await Shop.Ok(await Return(admin, orderId, itemId, 1, restock: false, reason: "Damaged"), "damaged return");
        Assert.Equal(stock, await Stock(f.A.ProductId));
        Assert.Equal(0m, await f.Scalar<decimal>("SELECT restocked_cost FROM order_returns WHERE company_id=@c AND order_id=@o", ("c", f.A.CompanyId), ("o", orderId)));
    }

    [Fact]
    public async Task Validation_RejectsBadRequests_AndOtherShopsOrders()
    {
        var a = await f.A.LoginAsync(); var b = await f.B.LoginAsync();
        var (orderId, itemId) = await Sale(a, 2);

        Assert.Equal(HttpStatusCode.BadRequest, (await a.PostAsJsonAsync("/api/returns", new { orderId, items = Array.Empty<object>(), refundMethod = "CASH" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Return(a, orderId, itemId, 0)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Return(a, orderId, itemId, -1)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Return(a, orderId, itemId, 1, method: "BITCOIN")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Return(a, orderId, itemId + 99999, 1)).StatusCode);     // an item that isn't on this order
        Assert.Equal(HttpStatusCode.BadRequest, (await Return(a, orderId, itemId, 1, method: "ADJUST_DUE")).StatusCode);   // walk-in sale: no account to adjust

        Assert.Equal(HttpStatusCode.NotFound, (await Return(b, orderId, itemId, 1)).StatusCode);                // another shop's order
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/returns/order/{orderId}")).StatusCode);
        Assert.DoesNotContain((await Shop.Body(await b.GetAsync("/api/returns"))).EnumerateArray(), x => x.GetProperty("orderId").GetInt32() == orderId);

        var (pending, _) = await f.A.PlaceOrder();                                                              // a web order that isn't completed
        Assert.Equal(HttpStatusCode.BadRequest, (await a.PostAsJsonAsync("/api/returns", new { orderId = pending, items = new[] { new { orderItemId = 1, quantity = 1, restock = true } }, refundMethod = "CASH" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.NewClient().GetAsync("/api/returns")).StatusCode);
    }

    [Fact]
    public async Task ReturningACreditSale_TakesItOffTheAccount_AndCashIsRefusedWhileMoneyIsStillOwed()
    {
        var admin = await f.A.LoginAsync();
        var phone = Phone();
        var (orderId, itemId) = await Sale(admin, 2, method: "CREDIT", phone: phone, name: "Jamal", paid: 500);   // owes 1500

        var cash = await Return(admin, orderId, itemId, 1, method: "CASH");
        Assert.Equal(HttpStatusCode.BadRequest, cash.StatusCode);
        Assert.Contains("owes", (await Shop.Body(cash)).GetProperty("message").GetString());

        await Shop.Ok(await Return(admin, orderId, itemId, 1, method: "ADJUST_DUE"), "adjust due");
        var bal = (await Shop.Body(await admin.GetAsync($"/api/ledger/customers/{phone}"))).GetProperty("balance").GetDecimal();
        Assert.Equal(500m, bal);                                                                                   // 1500 owed - 1000 returned
    }

    [Fact]
    public async Task Exchange_SwapsGoods_AndSettlesTheDifference()
    {
        var admin = await f.A.LoginAsync();
        var (orderId, itemId) = await Sale(admin, 1);
        var stock = await Stock(f.A.ProductId);

        // Swap 1 unit (1000) for 2 units (2000): customer pays 1000 more.
        var r = await Return(admin, orderId, itemId, 1, exchange: new[] { new { productId = f.A.ProductId, quantity = 2 } });
        await Shop.Ok(r, "exchange");
        var b = await Shop.Body(r);
        Assert.Equal("EXCHANGE", b.GetProperty("kind").GetString());
        Assert.Equal(1000m, b.GetProperty("customerPays").GetDecimal());
        Assert.Equal(0m, b.GetProperty("cashToGiveBack").GetDecimal());
        var newId = b.GetProperty("newOrderId").GetInt32();
        Assert.Equal(2000m, await f.Scalar<decimal>("SELECT total FROM orders WHERE id=@i", ("i", newId)));
        Assert.Equal("COMPLETED", await f.Scalar<string>("SELECT status FROM orders WHERE id=@i", ("i", newId)));
        Assert.Equal(stock + 1 - 2, await Stock(f.A.ProductId));                                                    // one came back, two went out
    }

    [Fact]
    public async Task Exchange_ForCheaperGoods_GivesTheRestBackInCash()
    {
        var admin = await f.A.LoginAsync();
        var cat = await f.Scalar<int>("SELECT id FROM categories WHERE company_id=@c ORDER BY id LIMIT 1", ("c", f.A.CompanyId));
        var name = "Cheap " + Guid.NewGuid().ToString("N")[..6];
        await Shop.Ok(await admin.PostAsJsonAsync("/api/products/batch", new { name, price = 400, wholesalePrice = 200, categoryId = cat, sizes = new[] { new { size = "X", quantity = 10 } } }), "cheap product");
        var cheapId = await f.Scalar<int>("SELECT id FROM products WHERE company_id=@c AND name=@n", ("c", f.A.CompanyId), ("n", $"{name} (Size X)"));

        var (orderId, itemId) = await Sale(admin, 1);                                                               // paid 1000
        var b = await Shop.Body(await Return(admin, orderId, itemId, 1, exchange: new[] { new { productId = cheapId, quantity = 1 } }));
        Assert.Equal(0m, b.GetProperty("customerPays").GetDecimal());
        Assert.Equal(600m, b.GetProperty("cashToGiveBack").GetDecimal());                                           // 1000 - 400
    }

    [Fact]
    public async Task ExchangeForSomethingOutOfStock_ChangesNothing()
    {
        var admin = await f.A.LoginAsync();
        var (orderId, itemId) = await Sale(admin, 1);
        var stock = await Stock(f.A.ProductId);
        var r = await Return(admin, orderId, itemId, 1, exchange: new[] { new { productId = f.A.ProductId, quantity = stock + 50 } });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal(stock, await Stock(f.A.ProductId));                                                           // the whole thing rolled back, including the restock
        Assert.Equal(0, await f.Scalar<int>("SELECT COUNT(*)::int FROM order_returns WHERE company_id=@c AND order_id=@o", ("c", f.A.CompanyId), ("o", orderId)));
    }

    [Fact]
    public async Task ParallelReturns_CannotReturnMoreThanWasSold()
    {
        var admin = await f.A.LoginAsync();
        var (orderId, itemId) = await Sale(admin, 2);
        var stock = await Stock(f.A.ProductId);
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => Return(admin, orderId, itemId, 1))));
        Assert.Equal(2, results.Count(x => x.StatusCode == HttpStatusCode.OK));
        Assert.Equal(stock + 2, await Stock(f.A.ProductId));
    }

    [Fact]
    public async Task AnOrderWithReturns_CannotBeCancelled_AndACancelledOrderCannotBeReturned()
    {
        var admin = await f.A.LoginAsync();
        var (o1, i1) = await Sale(admin, 2);
        await Shop.Ok(await Return(admin, o1, i1, 1), "return");
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/orders/{o1}/cancel", null)).StatusCode);     // would restock the returned unit twice

        var (o2, i2) = await Sale(admin, 1);
        await Shop.Ok(await admin.PostAsync($"/api/orders/{o2}/cancel", null), "cancel");
        Assert.Equal(HttpStatusCode.BadRequest, (await Return(admin, o2, i2, 1)).StatusCode);
    }

    [Fact]
    public async Task ProfitReport_NetsOutReturns()
    {
        var admin = await f.A.LoginAsync();
        var today = DateTime.UtcNow.AddHours(6).ToString("yyyy-MM-dd");
        async Task<(decimal Revenue, decimal Cogs)> P()
        {
            var s = (await Shop.Body(await admin.GetAsync($"/api/reports/profit?from={today}&to={today}"))).GetProperty("summary");
            return (s.GetProperty("revenue").GetDecimal(), s.GetProperty("cogs").GetDecimal());
        }
        var (orderId, itemId) = await Sale(admin, 3);
        var before = await P();
        await Shop.Ok(await Return(admin, orderId, itemId, 2), "return 2");                                         // 2000 refunded, 1200 cost back on the shelf
        var after = await P();
        Assert.Equal(before.Revenue - 2000m, after.Revenue);
        Assert.Equal(before.Cogs - 1200m, after.Cogs);

        await Shop.Ok(await Return(admin, orderId, itemId, 1, restock: false), "damaged one");                      // revenue falls, cost does not
        var last = await P();
        Assert.Equal(after.Revenue - 1000m, last.Revenue);
        Assert.Equal(after.Cogs, last.Cogs);
    }
}

[Collection("api")]
public class Batch10ReturnsFindTests(ApiFixture f)
{
    [Fact]
    public async Task FindByReceiptNumber_Works_AndNeverCrossesShops()
    {
        var a = await f.A.LoginAsync(); var b = await f.B.LoginAsync();
        var sale = await Shop.Body(await a.PostAsJsonAsync("/api/pos/checkout", new { items = new[] { new { productId = f.A.ProductId, quantity = 1, price = (decimal?)null } }, discount = 0, paymentMethod = "CASH" }));
        var number = sale.GetProperty("orderNumber").GetString()!;

        var hit = await Shop.Body(await a.GetAsync($"/api/returns/find?orderNumber={number}"));
        Assert.Equal(number, hit.GetProperty("order").GetProperty("orderNumber").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/returns/find?orderNumber={number}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.GetAsync("/api/returns/find?orderNumber=POS-000000")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.GetAsync("/api/returns/find?orderNumber=x")).StatusCode);
    }
}
