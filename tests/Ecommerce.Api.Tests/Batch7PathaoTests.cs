using System.Net;
using System.Net.Http.Json;

namespace Ecommerce.Api.Tests;

[Collection("api")]
public class Batch7PathaoTests(ApiFixture f)
{
    private static async Task Save(HttpClient admin, string store = "12345", string? sandbox = "true", string id = "CID", string pass = "PW")
    {
        var r = await admin.PutAsJsonAsync("/api/settings", new[]
        {
            new { key = "secret_pathao_client_id", value = id, groupName = "COURIER" },
            new { key = "secret_pathao_client_secret", value = "CSECRET-" + id, groupName = "COURIER" },
            new { key = "secret_pathao_username", value = id + "@shop.test", groupName = "COURIER" },
            new { key = "secret_pathao_password", value = pass, groupName = "COURIER" },
            new { key = "pathao_store_id", value = store, groupName = "COURIER" },
            new { key = "pathao_sandbox", value = sandbox ?? "", groupName = "COURIER" }
        });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Booking_NeedsCredentials_IsTenantScoped_AndBooksOnce()
    {
        var admin = await f.A.LoginAsync();
        var (orderId, orderNo) = await f.A.PlaceOrder(phone: "+8801812345679");

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync($"/api/courier/pathao/orders/{orderId}", null)).StatusCode);

        await Save(admin, store: "not-a-number");                                   // bad store id is refused too
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync($"/api/courier/pathao/orders/{orderId}", null)).StatusCode);
        await Save(admin, id: "A-ID", pass: "A-PW");

        var b = await f.B.LoginAsync();
        await Save(b, id: "B-ID");
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsync($"/api/courier/pathao/orders/{orderId}", null)).StatusCode);

        // Pathao needs a real address (10+ characters): refused before any call is made.
        var calls = f.Pathao.Bookings.Count;
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync($"/api/courier/pathao/orders/{orderId}", null)).StatusCode);
        Assert.Equal(calls, f.Pathao.Bookings.Count);
        await f.Exec("UPDATE orders SET shipping_address='House 12, Road 5, Dhanmondi' WHERE id=@i", ("i", orderId));

        f.Pathao.FailNext = true;
        Assert.Equal(HttpStatusCode.BadGateway, (await admin.PostAsync($"/api/courier/pathao/orders/{orderId}", null)).StatusCode);
        Assert.Equal("", await f.Scalar<string>("SELECT coalesce(courier_consignment_id,'') FROM orders WHERE id=@i", ("i", orderId)));

        var ok = await admin.PostAsync($"/api/courier/pathao/orders/{orderId}", null);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var body = await Shop.Body(ok);
        Assert.Equal("Pathao", body.GetProperty("courierName").GetString());
        Assert.StartsWith("PCID", body.GetProperty("trackingNumber").GetString());

        var sent = f.Pathao.Bookings.Last();
        Assert.Equal("A-ID", sent.Creds.ClientId);                 // this shop's credentials, not B's
        Assert.Equal("A-PW", sent.Creds.Password);
        Assert.Equal("12345", sent.Creds.StoreId);
        Assert.True(sent.Creds.Sandbox);
        Assert.Equal(orderNo, sent.Invoice);
        Assert.Equal("01812345679", sent.Phone);
        Assert.Equal(1000m, sent.Cod);

        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/courier/pathao/orders/{orderId}", null)).StatusCode);
        f.Pathao.StatusToReturn = "Delivered";
        var refreshed = await Shop.Body(await admin.GetAsync($"/api/courier/pathao/orders/{orderId}/status"));
        Assert.Equal("Delivered", refreshed.GetProperty("courierStatus").GetString());
    }

    [Fact]
    public async Task Status_RefusesOrdersBookedWithAnotherCourier_AndPaidOrdersCollectNothing()
    {
        var admin = await f.A.LoginAsync();
        await Save(admin);

        // An order already sent to Steadfast must not be queried through Pathao.
        var (steadId, _) = await f.A.PlaceOrder(phone: "01711000001");
        await f.Exec("UPDATE orders SET courier_name='Steadfast', courier_consignment_id='X1' WHERE id=@i", ("i", steadId));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync($"/api/courier/pathao/orders/{steadId}/status")).StatusCode);

        var (paidId, _) = await f.A.PlaceOrder(phone: "01711000002");
        await f.Exec("UPDATE orders SET payment_status='PAID', shipping_address='House 12, Road 5, Dhanmondi' WHERE id=@i", ("i", paidId));
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/courier/pathao/orders/{paidId}", null)).StatusCode);
        Assert.Equal(0m, f.Pathao.Bookings.Last().Cod);
    }

    [Fact]
    public async Task PathaoSecrets_AreNeverReturned_ButTheStoreIdIs()
    {
        var admin = await f.A.LoginAsync();
        await Save(admin, id: "VISIBLE-ID-CHECK", pass: "SUPER-SECRET-PW");
        var all = await admin.GetStringAsync("/api/settings");
        Assert.DoesNotContain("SUPER-SECRET-PW", all);
        Assert.DoesNotContain("CSECRET-VISIBLE-ID-CHECK", all);
        Assert.Contains("pathao_store_id", all);

        var pub = await f.A.Public.GetStringAsync("/api/settings/public");
        Assert.DoesNotContain("pathao", pub, StringComparison.OrdinalIgnoreCase);
    }
}
