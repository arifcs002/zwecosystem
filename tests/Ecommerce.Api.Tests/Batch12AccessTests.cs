using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ecommerce.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ecommerce.Api.Tests;

[Collection("api")]
public class Batch12AccessTests(ApiFixture f)
{
    // a new controller must be put in the policy table (or be public on purpose), otherwise it silently falls to Owner-only
    [Fact]
    public void Every_staff_controller_is_listed_in_the_access_policy()
    {
        var missing = typeof(Program).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .Where(t => !t.GetCustomAttributes(typeof(AllowAnonymousAttribute), true).Any())
            .Select(t => t.Name.Replace("Controller", ""))
            .Where(n => !AccessPolicy.Rules.ContainsKey(n))
            .ToList();
        Assert.True(missing.Count == 0, "Not in AccessPolicy: " + string.Join(", ", missing));
    }

    private async Task<HttpClient> StaffClient(Shop s, string role)
    {
        var email = $"{role}-{Guid.NewGuid():N}".Substring(0, 18) + "@t.test";
        var make = await s.Admin.PostAsJsonAsync("/api/users", new { companyId = s.CompanyId, email, password = "Passw0rd!x", firstName = "T", lastName = role, role, isActive = true });
        if (make.StatusCode == HttpStatusCode.Forbidden) throw new InvalidOperationException("create user blocked: " + await make.Content.ReadAsStringAsync());
        await Shop.Ok(make, "create " + role);
        var c = f.NewClient();
        var r = await c.PostAsJsonAsync("/api/auth/login", new { email, password = "Passw0rd!x", loginContext = s.Slug });
        await Shop.Ok(r, "login " + role);
        var token = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("sessionToken").GetString()!;
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    private static async Task<HttpStatusCode> Code(Task<HttpResponseMessage> t) => (await t).StatusCode;

    [Fact]
    public async Task Cashier_manager_and_owner_get_the_right_doors()
    {
        var shop = f.A;
        var owner = shop.Admin ?? await shop.LoginAsync();
        var cashier = await StaffClient(shop, "salesstaff");
        var manager = await StaffClient(shop, "companymanager");

        // (path, minimum tier) for read endpoints
        var reads = new (string Path, string Tier)[]
        {
            ("/api/dashboard/stats", "staff"), ("/api/customers", "staff"), ("/api/ledger/summary", "staff"), ("/api/orders", "staff"),
            ("/api/products", "manager"), ("/api/inventory", "manager"), ("/api/suppliers", "manager"), ("/api/cashbook/closings", "manager"),
            ("/api/stockcounts", "manager"), ("/api/purchases", "manager"),
            ("/api/expenses", "owner"), ("/api/reports/profit", "owner"), ("/api/coupons", "owner"), ("/api/users", "owner"), ("/api/activity/log", "owner"),
        };
        foreach (var (path, tier) in reads)
        {
            var cashierOk = tier == "staff";
            var managerOk = tier != "owner";
            Assert.Equal(cashierOk ? false : true, (await Code(cashier.GetAsync(path))) == HttpStatusCode.Forbidden);
            Assert.Equal(managerOk ? false : true, (await Code(manager.GetAsync(path))) == HttpStatusCode.Forbidden);
            Assert.NotEqual(HttpStatusCode.Forbidden, await Code(owner.GetAsync(path)));
        }

        // writes: a cashier can't add products or change settings; a manager can't touch owner-only money settings
        var prod = new { name = "x", price = 1, stock = 1 };
        Assert.Equal(HttpStatusCode.Forbidden, await Code(cashier.PostAsJsonAsync("/api/products", prod)));
        Assert.Equal(HttpStatusCode.Forbidden, await Code(cashier.PutAsJsonAsync("/api/settings", new { })));
        Assert.Equal(HttpStatusCode.Forbidden, await Code(manager.PutAsJsonAsync("/api/settings", new { })));
        Assert.Equal(HttpStatusCode.Forbidden, await Code(manager.PostAsJsonAsync("/api/coupons", new { })));
        Assert.Equal(HttpStatusCode.Forbidden, await Code(manager.PostAsJsonAsync("/api/ledger/adjustments", new { phone = "01711111111", amount = 1, note = "x" })));
        Assert.Equal(HttpStatusCode.Forbidden, await Code(cashier.PostAsJsonAsync("/api/ledger/remind-all", new { minDue = 1 })));
        Assert.Equal(HttpStatusCode.Forbidden, await Code(cashier.PostAsJsonAsync("/api/orders/1/cancel", new { })));
        Assert.Equal(HttpStatusCode.Forbidden, await Code(manager.PutAsJsonAsync("/api/refunds/1/approve", new { })));
        // a cashier CAN do the till work: sell, and send a single reminder is allowed to reach the controller (not 403)
        Assert.NotEqual(HttpStatusCode.Forbidden, await Code(cashier.GetAsync("/api/pos/catalog")));
        Assert.NotEqual(HttpStatusCode.Forbidden, await Code(cashier.PostAsJsonAsync("/api/ledger/payments", new { phone = "01700000000", amount = 1, method = "CASH" })));
        // a manager can do the stock work
        Assert.NotEqual(HttpStatusCode.Forbidden, await Code(manager.PostAsJsonAsync("/api/stockcounts", new { })));
    }

    [Fact]
    public async Task Public_storefront_and_other_shops_are_unaffected()
    {
        // anonymous storefront endpoints stay open
        Assert.Equal(HttpStatusCode.OK, await Code(f.A.Public.GetAsync("/api/products/public")));
        // not signed in on a staff endpoint is still 401, not 403
        Assert.Equal(HttpStatusCode.Unauthorized, await Code(f.NewClient().GetAsync("/api/expenses")));
        // the owner of shop B still has full access
        var b = f.B.Admin ?? await f.B.LoginAsync();
        Assert.Equal(HttpStatusCode.OK, await Code(b.GetAsync("/api/expenses")));
    }

    [Fact]
    public async Task Csv_exports_are_scoped_safe_and_permissioned()
    {
        var shop = f.A;
        var owner = shop.Admin ?? await shop.LoginAsync();
        var cashier = await StaffClient(shop, "salesstaff");
        var manager = await StaffClient(shop, "companymanager");

        // a customer whose name tries to be an Excel formula, and who owes money
        var phone = "0178" + Random.Shared.Next(1000000, 9999999);
        var order = await f.A.Public.PostAsJsonAsync("/api/orders/public", new
        {
            items = new[] { new { productId = shop.ProductId, quantity = 1 } },
            customerName = "=HYPERLINK(\"http://evil\")", customerPhone = phone, shippingAddress = "Addr", paymentMethod = "COD"
        });
        order.EnsureSuccessStatusCode();
        await Shop.Ok(await owner.PostAsJsonAsync("/api/ledger/adjustments", new { phone, amount = 321, note = "test due" }), "adjust");

        var today = DateTime.UtcNow.AddHours(6).ToString("yyyy-MM-dd");
        var sales = await owner.GetAsync($"/api/reports/export/sales?from={today}&to={today}");
        Assert.Equal(HttpStatusCode.OK, sales.StatusCode);
        Assert.StartsWith("text/csv", sales.Content.Headers.ContentType!.ToString());
        var bytes = await sales.Content.ReadAsByteArrayAsync();
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());          // BOM so Excel reads Bangla
        var csv = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.Contains(phone, csv);
        Assert.DoesNotContain(",=HYPERLINK", csv);                                       // formula neutralised
        Assert.Contains("'=HYPERLINK", csv);

        var dues = await (await owner.GetAsync("/api/reports/export/dues")).Content.ReadAsStringAsync();
        Assert.Contains(phone, dues);
        Assert.Contains("321", dues);

        // shop B never sees shop A's customer
        var bOwner = f.B.Admin ?? await f.B.LoginAsync();
        Assert.DoesNotContain(phone, await (await bOwner.GetAsync("/api/reports/export/dues")).Content.ReadAsStringAsync());
        Assert.DoesNotContain(phone, await (await bOwner.GetAsync($"/api/reports/export/sales?from={today}&to={today}")).Content.ReadAsStringAsync());

        // permissions: cashier none; manager gets sales/dues but not the cost/profit/expense files
        Assert.Equal(HttpStatusCode.Forbidden, await Code(cashier.GetAsync("/api/reports/export/dues")));
        Assert.Equal(HttpStatusCode.OK, await Code(manager.GetAsync("/api/reports/export/dues")));
        Assert.Equal(HttpStatusCode.Forbidden, await Code(manager.GetAsync("/api/reports/export/stock")));
        Assert.Equal(HttpStatusCode.Forbidden, await Code(manager.GetAsync($"/api/reports/export/profit?from={today}&to={today}")));
        Assert.Equal(HttpStatusCode.OK, await Code(owner.GetAsync($"/api/reports/export/profit?from={today}&to={today}")));
        Assert.Equal(HttpStatusCode.OK, await Code(owner.GetAsync("/api/reports/export/stock")));

        // bad input
        Assert.Equal(HttpStatusCode.BadRequest, await Code(owner.GetAsync("/api/reports/export/sales?from=oops&to=oops")));

        // dashboard money: includes this customer's due; cashier is not allowed to see it
        var money = await Shop.Body(await manager.GetAsync("/api/dashboard/money"));
        Assert.True(money.GetProperty("customersOweTotal").GetDecimal() >= 321);
        Assert.Equal(HttpStatusCode.Forbidden, await Code(cashier.GetAsync("/api/dashboard/money")));
    }
}
