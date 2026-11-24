using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ecommerce.Api.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ecommerce.Api.Tests;

public class OfferEngineTests
{
    private static OfferDef BuyGet(int id, int p, int buy, int get) => new(id, "Buy" + buy, "BUY_GET", p, buy, get, 0, 0, 0, new());

    [Fact]
    public void Buy_two_get_one_free_counts_whole_groups_only()
    {
        var r = OfferEngine.Compute(new[] { BuyGet(1, 7, 2, 1) }, new[] { new CartLine(7, 7, 100m) });
        Assert.Equal(200m, r.Discount);                                   // 7 units = 2 groups of 3 -> 2 free
        Assert.Equal(0m, OfferEngine.Compute(new[] { BuyGet(1, 7, 2, 1) }, new[] { new CartLine(7, 2, 100m) }).Discount);
    }

    [Fact]
    public void Bundle_applies_per_complete_set_and_percent_needs_the_minimum()
    {
        var bundle = new OfferDef(1, "Set", "BUNDLE", null, 0, 0, 0, 0, 250m, new() { (1, 1), (2, 2) });   // regular 100 + 2*100 = 300 for 250
        var r = OfferEngine.Compute(new[] { bundle }, new[] { new CartLine(1, 2, 100m), new CartLine(2, 5, 100m) });
        Assert.Equal(100m, r.Discount);                                   // two complete sets (2x product1, 4x product2): 2 * 50
        var pct = new OfferDef(2, "Bulk", "QTY_PERCENT", 3, 0, 0, 3, 10m, 0, new());
        Assert.Equal(0m, OfferEngine.Compute(new[] { pct }, new[] { new CartLine(3, 2, 100m) }).Discount);
        Assert.Equal(30m, OfferEngine.Compute(new[] { pct }, new[] { new CartLine(3, 3, 100m) }).Discount);
    }

    [Fact]
    public void One_unit_is_never_discounted_by_two_offers_and_discount_is_capped()
    {
        var a = BuyGet(1, 7, 1, 1);                                       // buy 1 get 1 free
        var b = new OfferDef(2, "Bulk", "QTY_PERCENT", 7, 0, 0, 2, 50m, 0, new());
        var r = OfferEngine.Compute(new[] { a, b }, new[] { new CartLine(7, 2, 100m) });
        Assert.Equal(100m, r.Discount);                                   // offer 1 used both units; offer 2 finds none left
        Assert.Single(r.Labels);
        Assert.True(OfferEngine.Compute(new[] { new OfferDef(1, "x", "QTY_PERCENT", null, 0, 0, 2, 90m, 0, new()) }, new[] { new CartLine(7, 2, 100m) }).Discount <= 200m);
    }
}

[Collection("api")]
public class Batch14Tests(ApiFixture f)
{
    private static async Task<HttpStatusCode> Code(Task<HttpResponseMessage> t) => (await t).StatusCode;

    private async Task<HttpClient> StaffClient(Shop s, string role)
    {
        var email = $"{role}-{Guid.NewGuid():N}".Substring(0, 18) + "@t.test";
        await Shop.Ok(await s.Admin.PostAsJsonAsync("/api/users", new { companyId = s.CompanyId, email, password = "Passw0rd!x", firstName = "T", lastName = role, role, isActive = true }), "create " + role);
        var c = f.NewClient();
        var r = await c.PostAsJsonAsync("/api/auth/login", new { email, password = "Passw0rd!x", loginContext = s.Slug });
        await Shop.Ok(r, "login " + role);
        var token = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("sessionToken").GetString()!;
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    // a fresh product of this shop with its own stock, so the tests never fight over the shared fixture product
    private async Task<int> NewProduct(int stock, decimal price = 100)
    {
        var a = f.A.Admin ?? await f.A.LoginAsync();
        var cat = await f.Scalar<int>("SELECT id FROM categories WHERE company_id=@c ORDER BY id LIMIT 1", ("c", f.A.CompanyId));
        var name = "B14-" + Guid.NewGuid().ToString("N")[..8];
        await Shop.Ok(await a.PostAsJsonAsync("/api/products/batch", new { name, price, wholesalePrice = 60, categoryId = cat, sizes = new[] { new { size = "40", quantity = stock } } }), "product");
        return await f.Scalar<int>("SELECT id FROM products WHERE company_id=@c AND name LIKE @n ORDER BY id DESC LIMIT 1", ("c", f.A.CompanyId), ("n", name + "%"));
    }

    private Task<HttpResponseMessage> Sale(HttpClient c, int productId, int qty, bool offline = false) =>
        c.PostAsJsonAsync("/api/pos/checkout", new { items = new[] { new { productId, quantity = qty, price = (decimal?)null } }, discount = 0, paymentMethod = "CASH", offline });

    [Fact]
    public async Task Offers_apply_at_the_till_and_online_and_respect_permissions()
    {
        var a = f.A.Admin ?? await f.A.LoginAsync();
        var cashier = await StaffClient(f.A, "salesstaff");
        var pid = await NewProduct(50, 1000);
        var created = new List<int>();
        try
        {
            Assert.Equal(HttpStatusCode.Forbidden, await Code(cashier.PostAsJsonAsync("/api/offers", new { name = "x", type = "BUY_GET", productId = pid, buyQty = 2, getQty = 1, isActive = true })));
            Assert.Equal(HttpStatusCode.BadRequest, await Code(a.PostAsJsonAsync("/api/offers", new { name = "bad", type = "BUY_GET", productId = pid, buyQty = 0, getQty = 1, isActive = true })));
            var other = await NewProduct(5, 100);
            Assert.Equal(HttpStatusCode.BadRequest, await Code(a.PostAsJsonAsync("/api/offers", new { name = "no-deal", type = "BUNDLE", bundlePrice = 5000, isActive = true, items = new[] { new { productId = pid, quantity = 1 }, new { productId = other, quantity = 1 } } })));

            var ok = await a.PostAsJsonAsync("/api/offers", new { name = "Buy 2 get 1", type = "BUY_GET", productId = pid, buyQty = 2, getQty = 1, isActive = true });
            await Shop.Ok(ok, "offer");
            var offerId = (await Shop.Body(ok)).GetProperty("id").GetInt32(); created.Add(offerId);

            // preview for the cashier, and the real sale
            var prev = await Shop.Body(await cashier.PostAsJsonAsync("/api/offers/preview", new { items = new[] { new { productId = pid, quantity = 3 } } }));
            Assert.Equal(1000m, prev.GetProperty("discount").GetDecimal());
            var sale = await Shop.Body(await Sale(cashier, pid, 3));
            var rc = sale.GetProperty("receipt");
            Assert.Equal(1000m, rc.GetProperty("discount").GetDecimal());
            Assert.Equal(2000m, rc.GetProperty("total").GetDecimal());
            Assert.Contains("Offer", await f.Scalar<string>("SELECT discount_label FROM orders WHERE id=@i", ("i", sale.GetProperty("orderId").GetInt32())));

            // an offline-synced sale keeps the price the customer already paid
            var off = await Shop.Body(await Sale(cashier, pid, 3, offline: true));
            Assert.Equal(0m, off.GetProperty("receipt").GetProperty("discount").GetDecimal());

            // online cart: public preview and the real order
            var pub = await Shop.Body(await f.A.Public.PostAsJsonAsync("/api/offers/public/preview", new { items = new[] { new { productId = pid, quantity = 3 } } }));
            Assert.Equal(1000m, pub.GetProperty("discount").GetDecimal());
            var web = await f.A.Public.PostAsJsonAsync("/api/orders/public", new { items = new[] { new { productId = pid, quantity = 3 } }, customerName = "B", customerPhone = "01711112222", shippingAddress = "x", paymentMethod = "COD" });
            await Shop.Ok(web, "web order");
            Assert.Equal(1000m, (await Shop.Body(web)).GetProperty("discount").GetDecimal());

            // other shop never sees this offer; switching it off stops it
            var b = f.B.Admin ?? await f.B.LoginAsync();
            Assert.DoesNotContain((await Shop.Body(await b.GetAsync("/api/offers"))).EnumerateArray(), o => o.GetProperty("id").GetInt32() == offerId);
            Assert.Equal(HttpStatusCode.OK, await Code(a.PutAsJsonAsync($"/api/offers/{offerId}", new { name = "Buy 2 get 1", type = "BUY_GET", productId = pid, buyQty = 2, getQty = 1, isActive = false })));
            Assert.Equal(0m, (await Shop.Body(await cashier.PostAsJsonAsync("/api/offers/preview", new { items = new[] { new { productId = pid, quantity = 3 } } }))).GetProperty("discount").GetDecimal());
        }
        finally { foreach (var id in created) await a.DeleteAsync($"/api/offers/{id}"); }
    }

    [Fact]
    public async Task Expiry_lots_block_expired_sales_use_fefo_and_can_be_written_off()
    {
        var a = f.A.Admin ?? await f.A.LoginAsync();
        var cashier = await StaffClient(f.A, "salesstaff");
        var pid = await NewProduct(20);
        var yesterday = DateTime.UtcNow.AddHours(6).AddDays(-1).ToString("yyyy-MM-dd");
        var soon = DateTime.UtcNow.AddHours(6).AddDays(10).ToString("yyyy-MM-dd");

        var oldResp = await a.PostAsJsonAsync("/api/batches", new { productId = pid, batchNo = "OLD", expiryDate = yesterday, quantity = 5 });
        await Shop.Ok(oldResp, "old lot");
        var old = await Shop.Body(oldResp);
        var fresh = await Shop.Body(await a.PostAsJsonAsync("/api/batches", new { productId = pid, batchNo = "FRESH", expiryDate = soon, quantity = 10 }));
        var oldId = old.GetProperty("id").GetInt32(); var freshId = fresh.GetProperty("id").GetInt32();
        // only 5 of the 20 units are not in a lot yet
        Assert.Equal(HttpStatusCode.BadRequest, await Code(a.PostAsJsonAsync("/api/batches", new { productId = pid, expiryDate = soon, quantity = 6 })));

        var exp = await Shop.Body(await a.GetAsync("/api/batches/expiring?days=30"));
        Assert.Contains(exp.GetProperty("expired").EnumerateArray(), x => x.GetProperty("id").GetInt32() == oldId);
        Assert.Contains(exp.GetProperty("soon").EnumerateArray(), x => x.GetProperty("id").GetInt32() == freshId);
        var alerts = await Shop.Body(await a.GetAsync("/api/alerts"));
        Assert.Contains(alerts.GetProperty("alerts").EnumerateArray(), x => x.GetProperty("text").GetString()!.Contains("expired"));

        // 20 in stock but 5 are expired: only 15 can be sold
        Assert.Equal(HttpStatusCode.BadRequest, await Code(Sale(cashier, pid, 16)));
        Assert.True((await Sale(cashier, pid, 15)).IsSuccessStatusCode);
        var left = await Shop.Body(await a.GetAsync($"/api/batches?productId={pid}"));
        Assert.DoesNotContain(left.EnumerateArray(), x => x.GetProperty("id").GetInt32() == freshId);      // the fresh lot was used up first
        Assert.Contains(left.EnumerateArray(), x => x.GetProperty("id").GetInt32() == oldId && x.GetProperty("quantity").GetInt32() == 5);   // expired lot untouched

        // write off: only managers; stock falls by the lot; a second try finds nothing
        Assert.Equal(HttpStatusCode.Forbidden, await Code(cashier.PostAsJsonAsync($"/api/batches/{oldId}/writeoff", new { note = "x" })));
        Assert.Equal(HttpStatusCode.OK, await Code(a.PostAsJsonAsync($"/api/batches/{oldId}/writeoff", new { note = "expired" })));
        Assert.Equal(0, await f.Scalar<int>("SELECT stock_quantity FROM products WHERE id=@p", ("p", pid)));
        Assert.Equal(HttpStatusCode.NotFound, await Code(a.PostAsJsonAsync($"/api/batches/{oldId}/writeoff", new { note = "again" })));
    }

    [Fact]
    public async Task Reorder_suggests_fast_sellers_and_counts_what_is_already_ordered()
    {
        var a = f.A.Admin ?? await f.A.LoginAsync();
        var pid = await NewProduct(20);
        Assert.True((await Sale(a, pid, 15)).IsSuccessStatusCode);                                   // 15 sold, 5 left

        async Task<JsonElement?> Find()
        {
            var r = await Shop.Body(await a.GetAsync("/api/reorder/suggestions?windowDays=30&coverDays=30&warnDays=20"));
            foreach (var x in r.GetProperty("items").EnumerateArray()) if (x.GetProperty("productId").GetInt32() == pid) return x;
            return null;
        }
        var hit = await Find();
        Assert.NotNull(hit);
        Assert.Equal(10, hit!.Value.GetProperty("suggestedQty").GetInt32());                          // 0.5/day * 30 days = 15, minus 5 in stock

        var sup = await a.PostAsJsonAsync("/api/suppliers", new { name = "Sup " + Guid.NewGuid().ToString("N")[..5], phoneNumber = "01700000001" });
        await Shop.Ok(sup, "supplier");
        var supId = (await Shop.Body(sup)).GetProperty("id").GetInt32();
        await Shop.Ok(await a.PostAsJsonAsync("/api/purchases", new { supplierId = supId, items = new[] { new { productId = pid, quantity = 10, unitCost = 60 } } }), "purchase order");
        Assert.Null(await Find());                                                                    // 10 already on order: nothing more to suggest

        Assert.Equal(HttpStatusCode.Forbidden, await Code((await StaffClient(f.A, "salesstaff")).GetAsync("/api/reorder/suggestions")));
    }

    [Fact]
    public async Task Daily_summary_is_sent_once_per_evening_and_alerts_are_for_managers()
    {
        var a = f.A.Admin ?? await f.A.LoginAsync();
        var phone = "0171" + Random.Shared.Next(1000000, 9999999);
        await Shop.Ok(await a.PutAsJsonAsync("/api/settings", new[] {
            new { key = "sms_api_url", value = "https://sms.example.test/send?to={to}&msg={message}", groupName = "SMS" },
            new { key = "daily_summary_phone", value = phone, groupName = "SUMMARY" },
            new { key = "daily_summary_enabled", value = "1", groupName = "SUMMARY" } }), "settings");
        try
        {
            // the owner can preview the message
            f.Sms.Sent.Clear();
            var test = await Shop.Body(await a.PostAsync("/api/alerts/summary/test", null));
            Assert.True(test.GetProperty("sent").GetBoolean());
            Assert.Contains("Sales", test.GetProperty("message").GetString());
            Assert.Equal(HttpStatusCode.Forbidden, await Code((await StaffClient(f.A, "companymanager")).PostAsync("/api/alerts/summary/test", null)));
            Assert.Equal(HttpStatusCode.Forbidden, await Code((await StaffClient(f.A, "salesstaff")).GetAsync("/api/alerts")));

            // the evening job: nothing before 9pm, exactly one message after, none on a repeat or restart
            await f.Exec("DELETE FROM daily_summaries WHERE company_id=@c", ("c", f.A.CompanyId));
            f.Sms.Sent.Clear();
            var svc = new DailySummaryService(f.Factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<DailySummaryService>.Instance);
            var day = DateTime.UtcNow.Date;
            await svc.RunOnceAsync(day.AddHours(10), CancellationToken.None);                         // 16:00 Bangladesh time
            Assert.DoesNotContain(f.Sms.Sent, s => s.To == phone);
            await svc.RunOnceAsync(day.AddHours(16), CancellationToken.None);                         // 22:00 Bangladesh time
            Assert.Single(f.Sms.Sent, s => s.To == phone);
            await svc.RunOnceAsync(day.AddHours(16).AddMinutes(5), CancellationToken.None);
            Assert.Single(f.Sms.Sent, s => s.To == phone);
        }
        finally
        {
            await a.PutAsJsonAsync("/api/settings", new[] { new { key = "daily_summary_enabled", value = "0", groupName = "SUMMARY" } });
            await f.Exec("DELETE FROM daily_summaries WHERE company_id=@c", ("c", f.A.CompanyId));
        }
    }
}
