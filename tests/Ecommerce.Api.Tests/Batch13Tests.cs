using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ecommerce.Api.Tests;

[Collection("api")]
public class Batch13Tests(ApiFixture f)
{
    private static string NewPhone() => "0151" + Random.Shared.Next(1000000, 9999999);
    private static async Task<HttpStatusCode> Code(Task<HttpResponseMessage> t) => (await t).StatusCode;

    private async Task<HttpClient> StaffClient(Shop s, string role)
    {
        var email = $"{role}-{Guid.NewGuid():N}@t.test";
        await Shop.Ok(await s.Admin.PostAsJsonAsync("/api/users", new { companyId = s.CompanyId, email, password = "Passw0rd!x", firstName = "Cash", lastName = role, role, isActive = true }), "create " + role);
        var c = f.NewClient();
        var r = await c.PostAsJsonAsync("/api/auth/login", new { email, password = "Passw0rd!x", loginContext = s.Slug });
        await Shop.Ok(r, "login " + role);
        var token = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("sessionToken").GetString()!;
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    private async Task<int> Points(HttpClient c, string phone) =>
        (await Shop.Body(await c.GetAsync($"/api/loyalty/lookup?phone={phone}"))).GetProperty("balance").GetInt32();

    [Fact]
    public async Task Online_orders_earn_points_when_completed_once_and_lose_them_on_cancel()
    {
        var a = f.A.Admin ?? await f.A.LoginAsync();
        await Shop.Ok(await a.PutAsJsonAsync("/api/loyalty/settings", new { enabled = true, earnPer100 = 2, pointValue = 1, minRedeem = 10 }), "enable");
        try
        {
            var phone = NewPhone();
            var (id, _) = await f.A.PlaceOrder(phone);                       // 1000 taka (+ shipping, which doesn't earn)
            Assert.Equal(0, await Points(a, phone));                         // placed, not completed: nothing yet

            Assert.True((await a.PutAsJsonAsync($"/api/orders/{id}/status", new { status = "COMPLETED" })).IsSuccessStatusCode);
            var earned = await Points(a, phone);
            Assert.Equal(20, earned);
            Assert.True((await a.PutAsJsonAsync($"/api/orders/{id}/status", new { status = "COMPLETED" })).IsSuccessStatusCode);
            Assert.Equal(earned, await Points(a, phone));                    // same status again: no double points

            Assert.True((await a.PutAsJsonAsync($"/api/orders/{id}/status", new { status = "CANCELLED" })).IsSuccessStatusCode);
            Assert.Equal(0, await Points(a, phone));
        }
        finally { await a.PutAsJsonAsync("/api/loyalty/settings", new { enabled = false, earnPer100 = 2, pointValue = 1, minRedeem = 10 }); }
    }

    [Fact]
    public async Task Cashier_shift_tracks_own_cash_and_manager_sees_the_report()
    {
        var cashier = await StaffClient(f.A, "salesstaff");
        var manager = await StaffClient(f.A, "companymanager");

        Assert.False((await Shop.Body(await cashier.GetAsync("/api/shifts/current"))).GetProperty("open").GetBoolean());
        Assert.True((await cashier.PostAsJsonAsync("/api/shifts/open", new { openingCash = 500 })).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.Conflict, await Code(cashier.PostAsJsonAsync("/api/shifts/open", new { openingCash = 1 })));

        // one 1000-taka cash sale by this cashier
        var sale = await cashier.PostAsJsonAsync("/api/pos/checkout", new { items = new[] { new { productId = f.A.ProductId, quantity = 1, price = (decimal?)null } }, discount = 0, paymentMethod = "CASH" });
        await Shop.Ok(sale, "sale");
        var cur = await Shop.Body(await cashier.GetAsync("/api/shifts/current"));
        Assert.Equal(1500m, cur.GetProperty("figures").GetProperty("expectedCash").GetDecimal());

        // short by 100: needs a reason
        Assert.Equal(HttpStatusCode.BadRequest, await Code(cashier.PostAsJsonAsync("/api/shifts/close", new { countedCash = 1400 })));
        var close = await Shop.Body(await cashier.PostAsJsonAsync("/api/shifts/close", new { countedCash = 1400, note = "gave wrong change" }));
        Assert.Equal(-100m, close.GetProperty("difference").GetDecimal());
        Assert.Equal(HttpStatusCode.NotFound, await Code(cashier.PostAsJsonAsync("/api/shifts/close", new { countedCash = 1400, note = "x" })));   // already closed

        var today = DateTime.UtcNow.AddHours(6).ToString("yyyy-MM-dd");
        var hist = await Shop.Body(await manager.GetAsync($"/api/shifts/history?from={today}&to={today}"));
        Assert.Contains(hist.EnumerateArray(), s => s.GetProperty("countedCash").ValueKind == JsonValueKind.Number && s.GetProperty("countedCash").GetDecimal() == 1400m);
        var rep = await Shop.Body(await manager.GetAsync($"/api/shifts/cashiers?from={today}&to={today}"));
        Assert.Contains(rep.EnumerateArray(), r => r.GetProperty("userName").GetString()!.Contains("salesstaff") && r.GetProperty("orders").GetInt32() >= 1);

        // permissions
        Assert.Equal(HttpStatusCode.Forbidden, await Code(cashier.GetAsync($"/api/shifts/history?from={today}&to={today}")));
        Assert.Equal(HttpStatusCode.Forbidden, await Code(cashier.GetAsync($"/api/shifts/cashiers?from={today}&to={today}")));
        // another shop's manager sees nothing of this
        var b = f.B.Admin ?? await f.B.LoginAsync();
        var bRep = await Shop.Body(await b.GetAsync($"/api/shifts/cashiers?from={today}&to={today}"));
        Assert.DoesNotContain(bRep.EnumerateArray(), r => r.GetProperty("userName").GetString()!.Contains("salesstaff"));
    }

    [Fact]
    public async Task Sms_campaign_is_previewed_confirmed_limited_and_respects_opt_outs()
    {
        var a = f.A.Admin ?? await f.A.LoginAsync();
        var manager = await StaffClient(f.A, "companymanager");
        await f.Exec("DELETE FROM sms_campaigns WHERE company_id=@c", ("c", f.A.CompanyId));
        await Shop.Ok(await a.PutAsJsonAsync("/api/settings", new[] { new { key = "sms_api_url", value = "https://sms.example.test/send?to={to}&msg={message}", groupName = "SMS" } }), "gateway");
        await Shop.Ok(await a.PutAsJsonAsync("/api/loyalty/settings", new { enabled = true, earnPer100 = 1, pointValue = 1, minRedeem = 10 }), "loyalty");
        try
        {
            var p1 = NewPhone(); var p2 = NewPhone(); var p3 = NewPhone();
            foreach (var p in new[] { p1, p2, p3 })
                Assert.True((await a.PostAsJsonAsync("/api/loyalty/adjustments", new { phone = p, points = 40, note = "seed" })).IsSuccessStatusCode);
            Assert.True((await a.PostAsJsonAsync("/api/sms-campaigns/optouts", new { phone = p3 })).IsSuccessStatusCode);

            // only the owner may use it
            Assert.Equal(HttpStatusCode.Forbidden, await Code(manager.PostAsJsonAsync("/api/sms-campaigns/preview", new { audience = "points", message = "Hi" })));

            var msg = "Hello {name}, 10% off this week!";
            var prev = await Shop.Body(await a.PostAsJsonAsync("/api/sms-campaigns/preview", new { audience = "points", message = msg }));
            var count = prev.GetProperty("count").GetInt32();
            Assert.True(prev.GetProperty("hasGateway").GetBoolean());
            Assert.True(count >= 2);

            // validation + stale preview
            Assert.Equal(HttpStatusCode.BadRequest, await Code(a.PostAsJsonAsync("/api/sms-campaigns/send", new { name = "x", audience = "points", message = "", expectedCount = count })));
            Assert.Equal(HttpStatusCode.BadRequest, await Code(a.PostAsJsonAsync("/api/sms-campaigns/send", new { name = "x", audience = "nobody", message = msg, expectedCount = count })));
            Assert.Equal(HttpStatusCode.Conflict, await Code(a.PostAsJsonAsync("/api/sms-campaigns/send", new { name = "x", audience = "points", message = msg, expectedCount = count + 5 })));

            f.Sms.Sent.Clear();
            var send = await Shop.Body(await a.PostAsJsonAsync("/api/sms-campaigns/send", new { name = "Eid offer", audience = "points", message = msg, expectedCount = count }));
            Assert.Equal(count, send.GetProperty("sent").GetInt32());
            Assert.Contains(f.Sms.Sent, s => s.To == p1 && s.Message.StartsWith("Hello Customer"));
            Assert.Contains(f.Sms.Sent, s => s.To == p2);
            Assert.DoesNotContain(f.Sms.Sent, s => s.To == p3);                 // opted out

            // a second campaign straight away is refused
            Assert.Equal((HttpStatusCode)429, await Code(a.PostAsJsonAsync("/api/sms-campaigns/send", new { name = "again", audience = "points", message = msg, expectedCount = count })));
            Assert.Contains((await Shop.Body(await a.GetAsync("/api/sms-campaigns"))).EnumerateArray(), c => c.GetProperty("name").GetString() == "Eid offer");

            // another shop can't see this campaign
            var b = f.B.Admin ?? await f.B.LoginAsync();
            Assert.DoesNotContain((await Shop.Body(await b.GetAsync("/api/sms-campaigns"))).EnumerateArray(), c => c.GetProperty("name").GetString() == "Eid offer");
        }
        finally
        {
            await a.PutAsJsonAsync("/api/loyalty/settings", new { enabled = false, earnPer100 = 1, pointValue = 1, minRedeem = 10 });
            await f.Exec("DELETE FROM sms_campaigns WHERE company_id=@c", ("c", f.A.CompanyId));
        }
    }
}
