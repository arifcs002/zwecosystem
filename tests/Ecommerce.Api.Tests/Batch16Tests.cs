using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ecommerce.Api.Tests;

[Collection("api")]
public class Batch16Tests(ApiFixture f)
{
    private static async Task<HttpStatusCode> Code(Task<HttpResponseMessage> t) => (await t).StatusCode;
    private static string Today() => DateTime.UtcNow.AddHours(6).ToString("yyyy-MM-dd");

    private async Task<HttpClient> StaffClient(Shop s, string role)
    {
        var email = $"{role}-{Guid.NewGuid():N}@t.test";
        await Shop.Ok(await s.Admin.PostAsJsonAsync("/api/users", new { companyId = s.CompanyId, email, password = "Passw0rd!x", firstName = "T", lastName = role, role, isActive = true }), "create " + role);
        var c = f.NewClient();
        var r = await c.PostAsJsonAsync("/api/auth/login", new { email, password = "Passw0rd!x", loginContext = s.Slug });
        await Shop.Ok(r, "login " + role);
        var token = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("sessionToken").GetString()!;
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    private async Task<int> NewProduct(int stock, decimal price = 100)
    {
        var a = f.A.Admin ?? await f.A.LoginAsync();
        var cat = await f.Scalar<int>("SELECT id FROM categories WHERE company_id=@c ORDER BY id LIMIT 1", ("c", f.A.CompanyId));
        var name = "B16-" + Guid.NewGuid().ToString("N")[..8];
        await Shop.Ok(await a.PostAsJsonAsync("/api/products/batch", new { name, price, wholesalePrice = 60, categoryId = cat, sizes = new[] { new { size = "40", quantity = stock } } }), "product");
        return await f.Scalar<int>("SELECT id FROM products WHERE company_id=@c AND name LIKE @n ORDER BY id DESC LIMIT 1", ("c", f.A.CompanyId), ("n", name + "%"));
    }

    [Fact]
    public async Task Product_profit_spreads_discounts_subtracts_returns_and_stays_owner_only()
    {
        var a = f.A.Admin ?? await f.A.LoginAsync();
        var pid = await NewProduct(30, 100);                                      // price 100, cost 60
        var name = await f.Scalar<string>("SELECT name FROM products WHERE id=@p", ("p", pid));

        // 10 units, 100 off the whole bill: revenue 1000 - 100 = 900, cost 600
        var sale = await a.PostAsJsonAsync("/api/pos/checkout", new { items = new[] { new { productId = pid, quantity = 10, price = (decimal?)null } }, discount = 100, paymentMethod = "CASH" });
        await Shop.Ok(sale, "sale");
        var orderId = (await Shop.Body(sale)).GetProperty("orderId").GetInt32();

        async Task<JsonElement> Row()
        {
            var rows = await Shop.Body(await a.GetAsync($"/api/analytics/products?from={Today()}&to={Today()}&limit=500"));
            return rows.EnumerateArray().First(r => r.GetProperty("productId").GetInt32() == pid);
        }
        var row = await Row();
        Assert.Equal(10, row.GetProperty("units").GetInt32());
        Assert.Equal(900m, row.GetProperty("revenue").GetDecimal());
        Assert.Equal(600m, row.GetProperty("cost").GetDecimal());
        Assert.Equal(300m, row.GetProperty("profit").GetDecimal());
        Assert.Equal(33.3m, row.GetProperty("marginPercent").GetDecimal());

        // return 2 units: they were sold for 90 each, so revenue drops by 180 and cost by 120
        var info = await Shop.Body(await a.GetAsync($"/api/returns/order/{orderId}"));
        var itemId = info.GetProperty("items")[0].GetProperty("orderItemId").GetInt32();
        await Shop.Ok(await a.PostAsJsonAsync("/api/returns", new { orderId, items = new[] { new { orderItemId = itemId, quantity = 2, restock = true } }, refundMethod = "CASH" }), "return");
        row = await Row();
        Assert.Equal(8, row.GetProperty("units").GetInt32());
        Assert.Equal(2, row.GetProperty("returnedUnits").GetInt32());
        Assert.Equal(720m, row.GetProperty("revenue").GetDecimal());
        Assert.Equal(240m, row.GetProperty("profit").GetDecimal());

        // the CSV has the same product; another shop has none of it
        var csv = await (await a.GetAsync($"/api/reports/export/products?from={Today()}&to={Today()}")).Content.ReadAsStringAsync();
        Assert.Contains(name, csv);
        var b = f.B.Admin ?? await f.B.LoginAsync();
        Assert.DoesNotContain(name, await (await b.GetAsync($"/api/reports/export/products?from={Today()}&to={Today()}")).Content.ReadAsStringAsync());

        // cost and profit are for the owner only
        var manager = await StaffClient(f.A, "companymanager");
        Assert.Equal(HttpStatusCode.Forbidden, await Code(manager.GetAsync($"/api/analytics/products?from={Today()}&to={Today()}")));
        Assert.Equal(HttpStatusCode.Forbidden, await Code(manager.GetAsync("/api/analytics/dead-stock")));
        Assert.Equal(HttpStatusCode.Forbidden, await Code(manager.GetAsync($"/api/reports/export/products?from={Today()}&to={Today()}")));
        Assert.Equal(HttpStatusCode.BadRequest, await Code(a.GetAsync("/api/analytics/products?from=x&to=y")));
    }

    [Fact]
    public async Task Dead_stock_lists_old_unsold_stock_only()
    {
        var a = f.A.Admin ?? await f.A.LoginAsync();
        var stuck = await NewProduct(5, 100);       // will be made "old" and never sold
        var sold = await NewProduct(5, 100);        // old but sold just now
        await f.Exec("UPDATE products SET created_date = NOW() - INTERVAL '200 days' WHERE id IN (@a, @b)", ("a", stuck), ("b", sold));
        var sale = await a.PostAsJsonAsync("/api/pos/checkout", new { items = new[] { new { productId = sold, quantity = 1, price = (decimal?)null } }, discount = 0, paymentMethod = "CASH" });
        await Shop.Ok(sale, "sale");
        var fresh = await NewProduct(5, 100);       // new product: not "stuck" yet

        var r = await Shop.Body(await a.GetAsync("/api/analytics/dead-stock?days=60"));
        var ids = r.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("productId").GetInt32()).ToList();
        Assert.Contains(stuck, ids);
        Assert.DoesNotContain(sold, ids);
        Assert.DoesNotContain(fresh, ids);
        var row = r.GetProperty("items").EnumerateArray().First(x => x.GetProperty("productId").GetInt32() == stuck);
        Assert.Equal(300m, row.GetProperty("costValue").GetDecimal());           // 5 x 60
        Assert.True(row.GetProperty("daysSinceSale").GetInt32() >= 199);
        Assert.Contains("dead-stock", (await a.GetAsync("/api/reports/export/dead-stock")).Content.Headers.ContentDisposition!.FileName);
    }

    [Fact]
    public async Task Summary_splits_channels_payments_and_new_versus_repeat_customers()
    {
        var a = f.A.Admin ?? await f.A.LoginAsync();
        var phone = "0155" + Random.Shared.Next(1000000, 9999999);

        // an earlier completed order for this phone (yesterday) makes today's buyer a repeat customer
        var (first, _) = await f.A.PlaceOrder(phone);
        await Shop.Ok(await a.PutAsJsonAsync($"/api/orders/{first}/status", new { status = "COMPLETED" }), "complete first");
        await f.Exec("UPDATE orders SET created_date = NOW() - INTERVAL '30 hours' WHERE id=@i", ("i", first));
        var (second, _) = await f.A.PlaceOrder(phone);
        await Shop.Ok(await a.PutAsJsonAsync($"/api/orders/{second}/status", new { status = "COMPLETED" }), "complete second");
        var newPhone = "0154" + Random.Shared.Next(1000000, 9999999);
        var (third, _) = await f.A.PlaceOrder(newPhone);
        await Shop.Ok(await a.PutAsJsonAsync($"/api/orders/{third}/status", new { status = "COMPLETED" }), "complete third");

        var s = await Shop.Body(await a.GetAsync($"/api/analytics/summary?from={Today()}&to={Today()}"));
        Assert.True(s.GetProperty("orders").GetInt32() >= 2);
        Assert.True(s.GetProperty("repeatCustomers").GetInt32() >= 1);
        Assert.True(s.GetProperty("newCustomers").GetInt32() >= 1);
        Assert.True(s.GetProperty("averageOrder").GetDecimal() > 0);
        Assert.Contains(s.GetProperty("channels").EnumerateArray(), c => c.GetProperty("key").GetString() == "ECOMMERCE");
        Assert.Contains(s.GetProperty("payments").EnumerateArray(), c => c.GetProperty("key").GetString() == "COD");
        Assert.Contains(s.GetProperty("topCustomers").EnumerateArray(), c => c.GetProperty("phone").GetString() == phone);

        // yesterday's order alone is not "repeat": the first-ever buyer of a range is new
        var y = DateTime.UtcNow.AddHours(6).AddDays(-2).ToString("yyyy-MM-dd");
        var yd = DateTime.UtcNow.AddHours(6).AddDays(-1).ToString("yyyy-MM-dd");
        var past = await Shop.Body(await a.GetAsync($"/api/analytics/summary?from={y}&to={yd}"));
        Assert.True(past.GetProperty("orders").GetInt32() >= 1);
    }

    [Fact]
    public async Task Shopper_sees_own_order_items_and_points_only_with_a_verified_phone()
    {
        var a = f.A.Admin ?? await f.A.LoginAsync();
        var phone = "0153" + Random.Shared.Next(1000000, 9999999);
        await Shop.Ok(await a.PutAsJsonAsync("/api/loyalty/settings", new { enabled = true, earnPer100 = 2, pointValue = 1, minRedeem = 10 }), "enable loyalty");
        try
        {
            var reg = await f.A.Public.PostAsJsonAsync("/api/account/register", new { name = "Loyal Shopper", phone, password = "Passw0rd!x" });
            await Shop.Ok(reg, "register");
            var token = (await Shop.Body(reg)).GetProperty("token").GetString()!;
            var accountId = (await Shop.Body(reg)).GetProperty("account").GetProperty("id").GetInt32();

            HttpRequestMessage Req(string url) { var r = new HttpRequestMessage(HttpMethod.Get, url); r.Headers.Add("X-Customer-Token", token); return r; }

            // places an order while logged in, and the shop completes it
            var place = new HttpRequestMessage(HttpMethod.Post, "/api/orders/public");
            place.Headers.Add("X-Customer-Token", token);
            place.Content = JsonContent.Create(new { items = new[] { new { productId = f.A.ProductId, quantity = 1 } }, customerName = "Loyal Shopper", customerPhone = phone, shippingAddress = "Addr", paymentMethod = "COD" });
            var placed = await f.A.Public.SendAsync(place);
            await Shop.Ok(placed, "order");
            var number = (await Shop.Body(placed)).GetProperty("orderNumber").GetString()!;
            var orderId = (await Shop.Body(placed)).GetProperty("orderId").GetInt32();
            await Shop.Ok(await a.PutAsJsonAsync($"/api/orders/{orderId}/status", new { status = "COMPLETED" }), "complete");

            // order detail: items and timeline; someone else's order is not found
            var detail = await Shop.Body(await f.A.Public.SendAsync(Req($"/api/account/orders/{number}")));
            Assert.Equal(1, detail.GetProperty("items").GetArrayLength());
            Assert.True(detail.GetProperty("timeline").GetArrayLength() >= 1);
            var other = await f.A.Public.PostAsJsonAsync("/api/account/register", new { name = "Other Shopper", phone = "0152" + Random.Shared.Next(1000000, 9999999), password = "Passw0rd!x" });
            var otherToken = (await Shop.Body(other)).GetProperty("token").GetString()!;
            var steal = new HttpRequestMessage(HttpMethod.Get, $"/api/account/orders/{number}"); steal.Headers.Add("X-Customer-Token", otherToken);
            Assert.Equal(HttpStatusCode.NotFound, await Code(f.A.Public.SendAsync(steal)));
            Assert.Equal(HttpStatusCode.Unauthorized, await Code(f.A.Public.GetAsync($"/api/account/orders/{number}")));

            // unverified phone: points are hidden (a stranger could have typed any number)
            var hidden = await Shop.Body(await f.A.Public.SendAsync(Req("/api/account/loyalty")));
            Assert.True(hidden.GetProperty("enabled").GetBoolean());
            Assert.False(hidden.GetProperty("verified").GetBoolean());
            Assert.Equal(0, hidden.GetProperty("balance").GetInt32());

            // after verification the real balance and history show
            await f.Exec("UPDATE customer_accounts SET phone_verified_at = NOW() WHERE id=@i", ("i", accountId));
            var shown = await Shop.Body(await f.A.Public.SendAsync(Req("/api/account/loyalty")));
            Assert.True(shown.GetProperty("verified").GetBoolean());
            Assert.True(shown.GetProperty("balance").GetInt32() > 0);
            Assert.Contains(shown.GetProperty("entries").EnumerateArray(), e => e.GetProperty("type").GetString() == "EARN");

            // another shop's shopper-token view of this shop's loyalty: not signed in there
            Assert.Equal(HttpStatusCode.Unauthorized, await Code(f.B.Public.SendAsync(Req("/api/account/loyalty"))));
        }
        finally { await a.PutAsJsonAsync("/api/loyalty/settings", new { enabled = false, earnPer100 = 2, pointValue = 1, minRedeem = 10 }); }
    }
}
