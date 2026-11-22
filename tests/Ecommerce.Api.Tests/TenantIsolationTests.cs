using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ecommerce.Api.Tests;

/// <summary>
/// Cross-tenant guarantees. In a multi-tenant SaaS one broken filter exposes every shop's
/// customers, so each public/admin surface is asserted from the "wrong" tenant's side.
/// </summary>
[Collection("api")]
public class TenantIsolationTests(ApiFixture f)
{
    [Fact]
    public async Task OrderDetail_OfAnotherTenant_Is404_OnEverySessionCacheMiss()
    {
        // Regression: the session middleware used to build the request's DbContext before the
        // caller's tenant was known, so the FIRST request of a fresh session ran with no tenant filter.
        var (orderId, _) = await f.B.PlaceOrder();

        for (var i = 0; i < 5; i++)
        {
            var freshSession = await f.A.LoginAsync();          // new session token => cache miss on first call
            var detail = await freshSession.GetAsync($"/api/orders/{orderId}");
            Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);

            var freshAgain = await f.A.LoginAsync();
            var list = await freshAgain.GetFromJsonAsync<JsonElement>("/api/orders", ApiFixture.Json);
            Assert.DoesNotContain(list.EnumerateArray(), o => o.GetProperty("id").GetInt32() == orderId);
        }
    }

    [Fact]
    public async Task OrderStatusHistoryCourierAndCancel_OfAnotherTenant_AreNotFound()
    {
        var (orderId, _) = await f.B.PlaceOrder();
        var a = await f.A.LoginAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await a.GetAsync($"/api/orders/{orderId}/history")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.PutAsJsonAsync($"/api/orders/{orderId}/courier", new { courierName = "x", trackingNumber = "y" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.PutAsJsonAsync($"/api/orders/{orderId}/status", new { status = "CANCELLED" })).StatusCode);
    }

    [Fact]
    public async Task PublicProducts_WithoutTenantHeader_ListsNothing_AndWithHeader_OnlyOwn()
    {
        var anon = f.NewClient();
        var none = await anon.GetFromJsonAsync<JsonElement>("/api/products/public", ApiFixture.Json);
        Assert.Equal(0, none.GetArrayLength());

        var own = await f.A.Public.GetFromJsonAsync<JsonElement>("/api/products/public", ApiFixture.Json);
        Assert.All(own.EnumerateArray(), p => Assert.NotEqual(f.B.ProductId, p.GetProperty("id").GetInt32()));
        Assert.Contains(own.EnumerateArray(), p => p.GetProperty("id").GetInt32() == f.A.ProductId);
        Assert.All(own.EnumerateArray(), p => Assert.False(p.TryGetProperty("wholesalePrice", out _), "cost price must never reach the storefront"));
    }

    [Fact]
    public async Task Coupons_AreTenantScoped()
    {
        var code = "ISO" + Guid.NewGuid().ToString("N")[..8].ToUpper();
        var create = await (await f.A.LoginAsync()).PostAsJsonAsync("/api/coupons",
            new { code, discountType = "FIXED", discountValue = 50, minOrderAmount = 0, isActive = true });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var id = (await Shop.Body(create)).GetProperty("id").GetInt32();

        var b = await f.B.LoginAsync();
        var list = await b.GetFromJsonAsync<JsonElement>("/api/coupons", ApiFixture.Json);
        Assert.DoesNotContain(list.EnumerateArray(), c => c.GetProperty("code").GetString() == code);
        Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync($"/api/coupons/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PatchAsync($"/api/coupons/{id}/toggle", null)).StatusCode);

        // B's shopper can't redeem A's code, and can't place an order with it.
        var check = await f.B.Public.PostAsJsonAsync("/api/coupons/validate",
            new { code, items = new[] { new { productId = f.B.ProductId, quantity = 1 } } });
        Assert.False((await Shop.Body(check)).GetProperty("valid").GetBoolean());
        var order = await f.B.Public.PostAsJsonAsync("/api/orders/public", new
        {
            items = new[] { new { productId = f.B.ProductId, quantity = 1 } }, customerName = "x", customerPhone = "01711111111",
            shippingAddress = "a", paymentMethod = "COD", couponCode = code
        });
        Assert.Equal(HttpStatusCode.BadRequest, order.StatusCode);
    }

    [Fact]
    public async Task CouponUsageLimit_HoldsUnderConcurrentCheckouts()
    {
        var code = "RACE" + Guid.NewGuid().ToString("N")[..8].ToUpper();
        await (await f.A.LoginAsync()).PostAsJsonAsync("/api/coupons",
            new { code, discountType = "FIXED", discountValue = 100, minOrderAmount = 0, usageLimit = 1, isActive = true });

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            var r = await f.A.Public.PostAsJsonAsync("/api/orders/public", new
            {
                items = new[] { new { productId = f.A.ProductId, quantity = 1 } }, customerName = "x", customerPhone = "01711111111",
                shippingAddress = "a", paymentMethod = "COD", couponCode = code
            });
            return r.StatusCode;
        }));

        Assert.Equal(1, results.Count(s => s == HttpStatusCode.OK));
        Assert.Equal(1, await f.Scalar<int>("SELECT used_count FROM coupons WHERE code=@c AND company_id=@id", ("c", code), ("id", f.A.CompanyId)));
    }

    [Fact]
    public async Task OrderTracking_NeedsMatchingPhone_AndTheRightStore()
    {
        var (_, number) = await f.A.PlaceOrder(phone: "01855556666");

        var ok = await f.A.Public.GetAsync($"/api/orders/track?orderNumber={number}&phone=01855556666");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var body = await Shop.Body(ok);
        Assert.False(body.TryGetProperty("customerIp", out _));
        Assert.False(body.TryGetProperty("orderNotes", out _));

        Assert.Equal(HttpStatusCode.OK, (await f.A.Public.GetAsync($"/api/orders/track?orderNumber={number}&phone=%2B8801855556666")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await f.A.Public.GetAsync($"/api/orders/track?orderNumber={number}&phone=01799999999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await f.B.Public.GetAsync($"/api/orders/track?orderNumber={number}&phone=01855556666")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await f.NewClient().GetAsync($"/api/orders/track?orderNumber={number}&phone=01855556666")).StatusCode);
    }

    [Fact]
    public async Task AbandonedCarts_AreTenantScoped_AndRecoveredWhenOrderPlaced()
    {
        var session = "iso-" + Guid.NewGuid().ToString("N")[..12];
        var cap = await f.A.Public.PostAsJsonAsync("/api/abandoned-carts/capture", new
        {
            sessionKey = session, customerName = "Cart Person", customerPhone = "01922223333",
            items = new[] { new { productId = f.A.ProductId, quantity = 2 } }
        });
        Assert.Equal(HttpStatusCode.OK, cap.StatusCode);

        var b = await f.B.LoginAsync();
        var bList = await b.GetFromJsonAsync<JsonElement>("/api/abandoned-carts", ApiFixture.Json);
        Assert.DoesNotContain(bList.EnumerateArray(), c => c.GetProperty("customerPhone").GetString() == "01922223333");

        var a = await f.A.LoginAsync();
        var aList = await a.GetFromJsonAsync<JsonElement>("/api/abandoned-carts", ApiFixture.Json);
        var mine = aList.EnumerateArray().First(c => c.GetProperty("customerPhone").GetString() == "01922223333");
        var id = mine.GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.NotFound, (await b.PutAsJsonAsync($"/api/abandoned-carts/{id}/status", new { status = "DISMISSED" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync($"/api/abandoned-carts/{id}")).StatusCode);

        await f.A.PlaceOrder(phone: "01922223333");
        Assert.Equal("RECOVERED", await f.Scalar<string>("SELECT status FROM abandoned_carts WHERE id=@i", ("i", id)));
    }

    [Fact]
    public async Task TenantSettingsWrite_RequiresLogin_AndOwnCompany()
    {
        var body = new Dictionary<string, string> { ["THEME_COLOR"] = "#ff0000" };
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.NewClient().PostAsJsonAsync($"/api/tenant/{f.A.CompanyId}/settings", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await f.A.LoginAsync()).PostAsJsonAsync($"/api/tenant/{f.B.CompanyId}/settings", body)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await (await f.A.LoginAsync()).PostAsJsonAsync($"/api/tenant/{f.A.CompanyId}/settings", body)).StatusCode);
    }

    [Theory]
    [InlineData(-5, 0)]     // negative quantity would ADD stock and create negative revenue
    [InlineData(0, 0)]
    [InlineData(1, 999999)] // discount larger than the cart
    [InlineData(1, -500)]   // negative discount would inflate the total
    public async Task PosCheckout_RejectsInvalidQuantityAndDiscount(int quantity, decimal discount)
    {
        var admin = await f.A.LoginAsync();
        var before = await f.Scalar<int>("SELECT stock_quantity FROM products WHERE id=@p", ("p", f.A.ProductId));
        var r = await admin.PostAsJsonAsync("/api/pos/checkout", new
        {
            items = new[] { new { productId = f.A.ProductId, quantity } }, discount, paymentMethod = "CASH", customerName = ""
        });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal(before, await f.Scalar<int>("SELECT stock_quantity FROM products WHERE id=@p", ("p", f.A.ProductId)));
    }

    [Fact]
    public async Task AnonymousCannotReachAdminEndpoints()
    {
        var anon = f.NewClient();
        foreach (var path in new[] { "/api/orders", "/api/coupons", "/api/abandoned-carts", "/api/products", "/api/customers", "/api/users" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync(path)).StatusCode);
    }
}
