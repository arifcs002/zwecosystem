using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ecommerce.Api.Tests;

[Collection("api")]
public class Batch2Tests(ApiFixture f)
{
    private static string Today() => DateTime.UtcNow.AddHours(6).ToString("yyyy-MM-dd"); // Bangladesh calendar day

    // ── Expenses & profit ────────────────────────────────────────────────────

    [Fact]
    public async Task Expenses_ValidateInput_AndAreTenantScoped()
    {
        var a = await f.A.LoginAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await a.PostAsJsonAsync("/api/expenses", new { category = "", amount = 10, expenseDate = Today() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.PostAsJsonAsync("/api/expenses", new { category = "Rent", amount = 0, expenseDate = Today() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.PostAsJsonAsync("/api/expenses", new { category = "Rent", amount = -5, expenseDate = Today() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.PostAsJsonAsync("/api/expenses", new { category = "Rent", amount = 5, expenseDate = "2099-01-01" })).StatusCode);

        var ok = await a.PostAsJsonAsync("/api/expenses", new { category = "Rent", amount = 123.45, expenseDate = Today(), note = "iso" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var id = (await Shop.Body(ok)).GetProperty("id").GetInt32();

        var b = await f.B.LoginAsync();
        var list = await b.GetFromJsonAsync<JsonElement>("/api/expenses", ApiFixture.Json);
        Assert.DoesNotContain(list.EnumerateArray(), e => e.GetProperty("id").GetInt32() == id);
        Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync($"/api/expenses/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PutAsJsonAsync($"/api/expenses/{id}", new { category = "Rent", amount = 1, expenseDate = Today() })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.NewClient().GetAsync("/api/expenses")).StatusCode);
    }

    [Fact]
    public async Task ProfitReport_ComputesRevenueCostExpensesAndNet_PerTenant()
    {
        // Product: price 1000, wholesale 600. Two units completed => revenue 2000, cost 1200, gross 800.
        var admin = await f.A.LoginAsync();
        var today = Today();
        var before = await ProfitOf(admin, today);

        var (orderId, _) = await f.A.PlaceOrder(qty: 2);
        // Orders created via checkout aren't revenue until COMPLETED.
        Assert.Equal(before.Revenue, (await ProfitOf(admin, today)).Revenue);
        var done = await admin.PutAsJsonAsync($"/api/orders/{orderId}/status", new { status = "COMPLETED" });
        Assert.Equal(HttpStatusCode.OK, done.StatusCode);
        await admin.PostAsJsonAsync("/api/expenses", new { category = "Advertising", amount = 300, expenseDate = today });

        var after = await ProfitOf(admin, today);
        Assert.Equal(before.Revenue + 2000, after.Revenue);
        Assert.Equal(before.Cogs + 1200, after.Cogs);
        Assert.Equal(before.Expenses + 300, after.Expenses);
        Assert.Equal(after.Revenue - after.Cogs - after.Expenses, after.Net);

        // Another shop's report must not include any of it.
        var b = await f.B.LoginAsync();
        Assert.Equal(0, (await ProfitOf(b, today)).Revenue);

        // Range validation
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/reports/profit?from=2026-02-01&to=2026-01-01")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/reports/profit?from=2020-01-01&to=2026-01-01")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/reports/profit?from=garbage&to=2026-01-01")).StatusCode);
    }

    private static async Task<(decimal Revenue, decimal Cogs, decimal Expenses, decimal Net)> ProfitOf(HttpClient c, string day)
    {
        var j = await c.GetFromJsonAsync<JsonElement>($"/api/reports/profit?from={day}&to={day}", ApiFixture.Json);
        var s = j.GetProperty("summary");
        return (s.GetProperty("revenue").GetDecimal(), s.GetProperty("cogs").GetDecimal(),
                s.GetProperty("expenses").GetDecimal(), s.GetProperty("netProfit").GetDecimal());
    }

    // ── Reviews ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reviews_AreModerated_VerifiedBuyersGoLive_AndAreTenantScoped()
    {
        var anon = f.A.Public;
        Assert.Equal(HttpStatusCode.BadRequest, (await anon.PostAsJsonAsync("/api/reviews/public", new { productId = f.A.ProductId, reviewerName = "X", rating = 5 })).StatusCode);   // name too short
        Assert.Equal(HttpStatusCode.BadRequest, (await anon.PostAsJsonAsync("/api/reviews/public", new { productId = f.A.ProductId, reviewerName = "Rafi", rating = 6 })).StatusCode);   // bad rating
        Assert.Equal(HttpStatusCode.BadRequest, (await anon.PostAsJsonAsync("/api/reviews/public", new { productId = f.A.ProductId, reviewerName = "Rafi", rating = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.PostAsJsonAsync("/api/reviews/public", new { productId = f.B.ProductId, reviewerName = "Rafi", rating = 4 })).StatusCode);  // other tenant's product

        var pending = await anon.PostAsJsonAsync("/api/reviews/public", new { productId = f.A.ProductId, reviewerName = "Rafi", rating = 4, comment = "Nice <b>fit</b>" });
        Assert.Equal(HttpStatusCode.OK, pending.StatusCode);
        Assert.Equal("PENDING", (await Shop.Body(pending)).GetProperty("status").GetString());

        var publicList = await anon.GetFromJsonAsync<JsonElement>($"/api/reviews/public/product/{f.A.ProductId}", ApiFixture.Json);
        Assert.DoesNotContain(publicList.GetProperty("reviews").EnumerateArray(), r => r.GetProperty("reviewerName").GetString() == "Rafi");

        // Admin approves it → it appears and counts toward the average.
        var admin = await f.A.LoginAsync();
        var all = await admin.GetFromJsonAsync<JsonElement>("/api/reviews?status=PENDING", ApiFixture.Json);
        var id = all.EnumerateArray().First(r => r.GetProperty("reviewerName").GetString() == "Rafi").GetProperty("id").GetInt32();

        var b = await f.B.LoginAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await b.PutAsJsonAsync($"/api/reviews/{id}/status", new { status = "APPROVED" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync($"/api/reviews/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/reviews/{id}/status", new { status = "WHATEVER" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/reviews/{id}/status", new { status = "APPROVED" })).StatusCode);

        publicList = await anon.GetFromJsonAsync<JsonElement>($"/api/reviews/public/product/{f.A.ProductId}", ApiFixture.Json);
        Assert.Contains(publicList.GetProperty("reviews").EnumerateArray(), r => r.GetProperty("reviewerName").GetString() == "Rafi");

        // A verified purchase (order number + phone) is approved straight away and flagged verified.
        var (_, orderNo) = await f.A.PlaceOrder(phone: "01877778888");
        var verified = await anon.PostAsJsonAsync("/api/reviews/public", new
        {
            productId = f.A.ProductId, reviewerName = "Real Buyer", rating = 5, orderNumber = orderNo, phone = "01877778888"
        });
        var vb = await Shop.Body(verified);
        Assert.Equal("APPROVED", vb.GetProperty("status").GetString());
        Assert.True(vb.GetProperty("verified").GetBoolean());

        // Wrong phone → not verified → pending.
        var fake = await anon.PostAsJsonAsync("/api/reviews/public", new
        {
            productId = f.A.ProductId, reviewerName = "Faker", rating = 5, orderNumber = orderNo, phone = "01700000001"
        });
        Assert.Equal("PENDING", (await Shop.Body(fake)).GetProperty("status").GetString());

        // Summary exposes only this shop's keys; B sees none of A's.
        var summaryB = await f.B.Public.GetFromJsonAsync<JsonElement>("/api/reviews/public/summary", ApiFixture.Json);
        Assert.Equal(0, summaryB.EnumerateObject().Count());
        var summaryA = await f.A.Public.GetFromJsonAsync<JsonElement>("/api/reviews/public/summary", ApiFixture.Json);
        Assert.True(summaryA.EnumerateObject().Count() >= 1);
        Assert.Equal(0, (await f.NewClient().GetFromJsonAsync<JsonElement>("/api/reviews/public/summary", ApiFixture.Json)).EnumerateObject().Count());
    }

    // ── Courier & secrets ────────────────────────────────────────────────────

    [Fact]
    public async Task Courier_Booking_NeedsCredentials_IsTenantScoped_AndBooksOnce()
    {
        var admin = await f.A.LoginAsync();
        var (orderId, orderNo) = await f.A.PlaceOrder(phone: "+8801812345678");

        // No credentials yet.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync($"/api/courier/steadfast/orders/{orderId}", null)).StatusCode);

        await SaveSecrets(admin, "KEY-A", "SECRET-A");

        // Another shop can't book (or even see) this order.
        var b = await f.B.LoginAsync();
        await SaveSecrets(b, "KEY-B", "SECRET-B");
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsync($"/api/courier/steadfast/orders/{orderId}", null)).StatusCode);

        // Courier failure → 502 and the order is untouched.
        f.Courier.FailNext = true;
        Assert.Equal(HttpStatusCode.BadGateway, (await admin.PostAsync($"/api/courier/steadfast/orders/{orderId}", null)).StatusCode);
        Assert.Equal("", await f.Scalar<string>("SELECT coalesce(courier_consignment_id,'') FROM orders WHERE id=@i", ("i", orderId)));

        var before = f.Courier.Bookings.Count;
        var ok = await admin.PostAsync($"/api/courier/steadfast/orders/{orderId}", null);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var body = await Shop.Body(ok);
        Assert.Equal("Steadfast", body.GetProperty("courierName").GetString());
        Assert.StartsWith("TRK", body.GetProperty("trackingNumber").GetString());

        var sent = f.Courier.Bookings.Last();
        Assert.Equal(before + 1, f.Courier.Bookings.Count);
        Assert.Equal("KEY-A", sent.Key);                 // used THIS shop's credentials
        Assert.Equal("SECRET-A", sent.Secret);
        Assert.Equal(orderNo, sent.Invoice);
        Assert.Equal("01812345678", sent.Phone);         // +88 stripped to the canonical form
        Assert.Equal(1000m, sent.Cod);                   // unpaid COD → collect the full total

        // Second booking is refused; status refresh works; customers can see the courier status.
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/courier/steadfast/orders/{orderId}", null)).StatusCode);
        f.Courier.StatusToReturn = "delivered";
        var refreshed = await Shop.Body(await admin.GetAsync($"/api/courier/steadfast/orders/{orderId}/status"));
        Assert.Equal("delivered", refreshed.GetProperty("courierStatus").GetString());
        var tracked = await Shop.Body(await f.A.Public.GetAsync($"/api/orders/track?orderNumber={orderNo}&phone=01812345678"));
        Assert.Equal("delivered", tracked.GetProperty("courierStatus").GetString());

        // A cancelled order can't be booked.
        var (cancelId, _) = await f.A.PlaceOrder();
        await admin.PostAsync($"/api/orders/{cancelId}/cancel", null);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync($"/api/courier/steadfast/orders/{cancelId}", null)).StatusCode);
    }

    [Fact]
    public async Task SecretSettings_AreWriteOnly_AndNeverPublic()
    {
        var admin = await f.A.LoginAsync();
        await SaveSecrets(admin, "SUPER-SECRET-KEY", "SUPER-SECRET-VALUE");

        var settings = await admin.GetFromJsonAsync<JsonElement>("/api/settings", ApiFixture.Json);
        var key = settings.EnumerateArray().First(s => s.GetProperty("key").GetString() == "secret_steadfast_api_key");
        Assert.Equal("********", key.GetProperty("value").GetString());           // masked for the browser
        Assert.DoesNotContain("SUPER-SECRET", settings.GetRawText());

        var publicSettings = await f.A.Public.GetStringAsync("/api/settings/public");
        Assert.DoesNotContain("secret_", publicSettings);
        Assert.DoesNotContain("SUPER-SECRET", publicSettings);

        // Echoing the mask back must NOT overwrite the real value.
        await admin.PutAsJsonAsync("/api/settings", new[] { new { key = "secret_steadfast_api_key", value = "********", groupName = "COURIER" } });
        Assert.Equal("SUPER-SECRET-KEY", await f.Scalar<string>(
            "SELECT value FROM company_settings WHERE company_id=@c AND key='secret_steadfast_api_key'", ("c", f.A.CompanyId)));

        // Another shop's settings never include A's secret.
        var bSettings = await (await f.B.LoginAsync()).GetStringAsync("/api/settings");
        Assert.DoesNotContain("SUPER-SECRET", bSettings);
    }

    private static async Task SaveSecrets(HttpClient admin, string key, string secret)
    {
        var r = await admin.PutAsJsonAsync("/api/settings", new[]
        {
            new { key = "secret_steadfast_api_key", value = key, groupName = "COURIER" },
            new { key = "secret_steadfast_secret_key", value = secret, groupName = "COURIER" }
        });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }
}
