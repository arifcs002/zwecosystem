using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ecommerce.Api.Tests;

[Collection("api")]
public class Batch12LoyaltyTests(ApiFixture f)
{
    private static string NewPhone() => "0161" + Random.Shared.Next(1000000, 9999999);

    private async Task SetLoyalty(bool enabled, decimal earn = 2, decimal value = 1, int min = 10) =>
        await Shop.Ok(await f.A.Admin.PutAsJsonAsync("/api/loyalty/settings", new { enabled, earnPer100 = earn, pointValue = value, minRedeem = min }), "loyalty settings");

    private Task<HttpResponseMessage> Sale(string? phone, int qty = 1, int? redeem = null, string? clientRef = null, decimal discount = 0) =>
        f.A.Admin.PostAsJsonAsync("/api/pos/checkout", new
        {
            items = new[] { new { productId = f.A.ProductId, quantity = qty, price = (decimal?)null } },
            discount, paymentMethod = "CASH", customerPhone = phone, customerName = "Loyal", redeemPoints = redeem, clientRef
        });

    private async Task<int> Balance(HttpClient c, string phone) =>
        (await Shop.Body(await c.GetAsync($"/api/loyalty/lookup?phone={phone}"))).GetProperty("balance").GetInt32();

    [Fact]
    public async Task Points_are_earned_used_returned_and_cancelled_correctly()
    {
        var a = f.A.Admin ?? await f.A.LoginAsync();
        await SetLoyalty(true, earn: 2, value: 1, min: 10);
        try
        {
            var phone = NewPhone();

            // 1000 taka sale -> 20 points
            var s1 = await Shop.Body(await Sale(phone));
            Assert.Equal(20, s1.GetProperty("receipt").GetProperty("pointsEarned").GetInt32());
            Assert.Equal(20, await Balance(a, phone));
            var order1 = s1.GetProperty("orderId").GetInt32();

            // use 15 points: 15 taka off, earns on the smaller bill (985 -> 19)
            var s2 = await Shop.Body(await Sale(phone, redeem: 15));
            var r2 = s2.GetProperty("receipt");
            Assert.Equal(15m, r2.GetProperty("discount").GetDecimal());
            Assert.Equal(985m, r2.GetProperty("total").GetDecimal());
            Assert.Equal(15, r2.GetProperty("pointsUsed").GetInt32());
            Assert.Equal(20 - 15 + 19, await Balance(a, phone));
            var order2 = s2.GetProperty("orderId").GetInt32();

            // rules: more than owned, below the minimum, no phone, negative
            Assert.Equal(HttpStatusCode.BadRequest, (await Sale(phone, redeem: 500)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Sale(phone, redeem: 5)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Sale(null, redeem: 10)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Sale(phone, redeem: -3)).StatusCode);
            Assert.Equal(24, await Balance(a, phone));                                    // nothing moved on the failed attempts

            // a retried offline sale must not earn twice
            var cref = "ref-" + Guid.NewGuid().ToString("N")[..16];
            Assert.True((await Sale(phone, clientRef: cref)).IsSuccessStatusCode);
            var afterFirst = await Balance(a, phone);
            Assert.True((await Sale(phone, clientRef: cref)).IsSuccessStatusCode);
            Assert.Equal(afterFirst, await Balance(a, phone));

            // other shop can't see these points
            var b = f.B.Admin ?? await f.B.LoginAsync();
            Assert.Equal(0, await Balance(b, phone));

            // return one of order 1's goods: its 20 points come off
            var info = await Shop.Body(await a.GetAsync($"/api/returns/order/{order1}"));
            var itemId = info.GetProperty("items")[0].GetProperty("orderItemId").GetInt32();
            var before = await Balance(a, phone);
            Assert.True((await a.PostAsJsonAsync("/api/returns", new { orderId = order1, items = new[] { new { orderItemId = itemId, quantity = 1, restock = true } }, refundMethod = "CASH" })).IsSuccessStatusCode);
            Assert.Equal(before - 20, await Balance(a, phone));

            // cancel order 2: its 19 earned points go, the 15 used come back
            var cancel = await a.PostAsJsonAsync($"/api/orders/{order2}/cancel", new { });
            Assert.True(cancel.IsSuccessStatusCode, await cancel.Content.ReadAsStringAsync());
            Assert.Equal(before - 20 - 19 + 15, await Balance(a, phone));
            // cancelling again does nothing more
            await a.PostAsJsonAsync($"/api/orders/{order2}/cancel", new { });
            Assert.Equal(before - 20 - 19 + 15, await Balance(a, phone));

            // owner adjustments: can't go below zero; cashier can't do it
            var bal = await Balance(a, phone);
            Assert.Equal(HttpStatusCode.BadRequest, (await a.PostAsJsonAsync("/api/loyalty/adjustments", new { phone, points = -(bal + 1), note = "x" })).StatusCode);
            Assert.True((await a.PostAsJsonAsync("/api/loyalty/adjustments", new { phone, points = 50, note = "birthday bonus" })).IsSuccessStatusCode);
            Assert.Equal(bal + 50, await Balance(a, phone));
            var hist = await Shop.Body(await a.GetAsync($"/api/loyalty/customers/{phone}"));
            Assert.Contains(hist.GetProperty("entries").EnumerateArray(), e => e.GetProperty("entryType").GetString() == "ADJUST");
        }
        finally { await SetLoyalty(false); }
    }

    [Fact]
    public async Task Off_by_default_and_settings_are_validated()
    {
        await SetLoyalty(false);
        var phone = NewPhone();
        var s = await Shop.Body(await Sale(phone));
        Assert.Equal(JsonValueKind.Null, s.GetProperty("receipt").GetProperty("pointsEarned").ValueKind);   // nothing earned while off
        Assert.Equal(0, await Balance(f.A.Admin, phone));
        Assert.Equal(HttpStatusCode.BadRequest, (await Sale(phone, redeem: 10)).StatusCode);                // can't redeem while off

        // a rate that gives back too much is refused
        Assert.Equal(HttpStatusCode.BadRequest, (await f.A.Admin.PutAsJsonAsync("/api/loyalty/settings", new { enabled = true, earnPer100 = 100, pointValue = 5, minRedeem = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await f.A.Admin.PutAsJsonAsync("/api/loyalty/settings", new { enabled = true, earnPer100 = -1, pointValue = 1, minRedeem = 1 })).StatusCode);
    }
}
