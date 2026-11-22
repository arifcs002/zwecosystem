using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ecommerce.Api.Tests;

[Collection("api")]
public class Batch3Tests(ApiFixture f)
{
    private const string Pw = "Shopper#1234";
    private static int _phoneSeq = Random.Shared.Next(1_000_000, 80_000_000);
    // Unique valid BD mobile (01[3-9] + 8 digits) per call so tests never collide on the (shop, phone) rule.
    private static string NewPhone() => "017" + Interlocked.Increment(ref _phoneSeq).ToString("D8");

    private static HttpRequestMessage Req(HttpMethod m, string url, string? token, object? body = null)
    {
        var r = new HttpRequestMessage(m, url);
        if (token != null) r.Headers.Add("X-Customer-Token", token);
        if (body != null) r.Content = JsonContent.Create(body);
        return r;
    }

    private async Task<(string Token, string Phone, int Id)> Register(Shop shop, string? phone = null)
    {
        phone ??= NewPhone();
        var r = await shop.Public.PostAsJsonAsync("/api/account/register", new { name = "Test Shopper", phone, password = Pw });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var j = await Shop.Body(r);
        return (j.GetProperty("token").GetString()!, phone, j.GetProperty("account").GetProperty("id").GetInt32());
    }

    private async Task<(int Id, decimal Discount, string? Label)> OrderAs(Shop shop, string? token, string? coupon = null)
    {
        var r = await shop.Public.SendAsync(Req(HttpMethod.Post, "/api/orders/public", token, new
        {
            items = new[] { new { productId = shop.ProductId, quantity = 1 } }, customerName = "Buyer", customerPhone = "01711111111",
            shippingAddress = "Addr", paymentMethod = "COD", couponCode = coupon
        }));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var j = await Shop.Body(r);
        return (j.GetProperty("orderId").GetInt32(), j.GetProperty("discount").GetDecimal(),
                j.TryGetProperty("discountLabel", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString() : null);
    }

    // ── Customer accounts ────────────────────────────────────────────────────

    [Fact]
    public async Task Account_Registration_ValidatesInput_AndPhoneIsUniquePerShop()
    {
        var p = f.A.Public;
        Assert.Equal(HttpStatusCode.BadRequest, (await p.PostAsJsonAsync("/api/account/register", new { name = "A", phone = NewPhone(), password = Pw })).StatusCode);        // name too short
        Assert.Equal(HttpStatusCode.BadRequest, (await p.PostAsJsonAsync("/api/account/register", new { name = "Rafi", phone = "12345", password = Pw })).StatusCode);       // not a BD mobile
        Assert.Equal(HttpStatusCode.BadRequest, (await p.PostAsJsonAsync("/api/account/register", new { name = "Rafi", phone = NewPhone(), password = "short" })).StatusCode); // weak password
        Assert.Equal(HttpStatusCode.BadRequest, (await p.PostAsJsonAsync("/api/account/register", new { name = "Rafi", phone = NewPhone(), password = Pw, email = "nope" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await f.NewClient().PostAsJsonAsync("/api/account/register", new { name = "Rafi", phone = NewPhone(), password = Pw })).StatusCode); // no store

        var phone = NewPhone();
        await Register(f.A, phone);
        Assert.Equal(HttpStatusCode.Conflict, (await p.PostAsJsonAsync("/api/account/register", new { name = "Again", phone = "+88" + phone, password = Pw })).StatusCode);
        await Register(f.B, phone);   // the same number may have its own account in a different shop
    }

    [Fact]
    public async Task Account_Login_DoesNotRevealWhetherTheNumberExists_AndIsRateLimited()
    {
        var (_, phone, _) = await Register(f.A);
        var wrongPw = await f.A.Public.PostAsJsonAsync("/api/account/login", new { phone, password = "WrongPass#99" });
        var noUser = await f.A.Public.PostAsJsonAsync("/api/account/login", new { phone = NewPhone(), password = "WrongPass#99" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPw.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, noUser.StatusCode);
        Assert.Equal((await Shop.Body(wrongPw)).GetProperty("message").GetString(), (await Shop.Body(noUser)).GetProperty("message").GetString());

        Assert.Equal(HttpStatusCode.OK, (await f.A.Public.PostAsJsonAsync("/api/account/login", new { phone, password = Pw })).StatusCode);

        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 14; i++)
            codes.Add((await f.A.Public.PostAsJsonAsync("/api/account/login", new { phone, password = "bruteforce" + i })).StatusCode);
        Assert.Contains(HttpStatusCode.TooManyRequests, codes);   // a number can't be brute-forced
    }

    [Fact]
    public async Task CustomerToken_IsStoreScoped_NeverWorksAsStaffAuth_AndLogoutKillsIt()
    {
        var (token, phone, _) = await Register(f.A);
        Assert.Equal(HttpStatusCode.OK, (await f.A.Public.SendAsync(Req(HttpMethod.Get, "/api/account/me", token))).StatusCode);

        // Same token, different store header => not logged in.
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.B.Public.SendAsync(Req(HttpMethod.Get, "/api/account/me", token))).StatusCode);
        // No token / garbage token.
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.A.Public.SendAsync(Req(HttpMethod.Get, "/api/account/me", null))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.A.Public.SendAsync(Req(HttpMethod.Get, "/api/account/me", "x".PadRight(40, 'y')))).StatusCode);

        // A shopper token must not unlock a single staff endpoint, in either header.
        foreach (var path in new[] { "/api/orders", "/api/coupons", "/api/customer-groups/accounts", "/api/activity/log", "/api/expenses" })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await f.A.Public.SendAsync(Req(HttpMethod.Get, path, token))).StatusCode);
            var asBearer = Req(HttpMethod.Get, path, null);
            asBearer.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            Assert.Equal(HttpStatusCode.Unauthorized, (await f.A.Public.SendAsync(asBearer)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, (await f.A.Public.SendAsync(Req(HttpMethod.Post, "/api/account/logout", token))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.A.Public.SendAsync(Req(HttpMethod.Get, "/api/account/me", token))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await f.A.Public.PostAsJsonAsync("/api/account/login", new { phone, password = Pw })).StatusCode);
    }

    [Fact]
    public async Task ChangingPassword_SignsOtherDevicesOut()
    {
        var (t1, phone, _) = await Register(f.A);
        var login2 = await Shop.Body(await f.A.Public.PostAsJsonAsync("/api/account/login", new { phone, password = Pw }));
        var t2 = login2.GetProperty("token").GetString()!;

        Assert.Equal(HttpStatusCode.BadRequest, (await f.A.Public.SendAsync(Req(HttpMethod.Put, "/api/account/password", t1, new { currentPassword = "wrong", newPassword = "NewPass#5678" }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await f.A.Public.SendAsync(Req(HttpMethod.Put, "/api/account/password", t1, new { currentPassword = Pw, newPassword = "short" }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await f.A.Public.SendAsync(Req(HttpMethod.Put, "/api/account/password", t1, new { currentPassword = Pw, newPassword = "NewPass#5678" }))).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await f.A.Public.SendAsync(Req(HttpMethod.Get, "/api/account/me", t1))).StatusCode);          // this device stays in
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.A.Public.SendAsync(Req(HttpMethod.Get, "/api/account/me", t2))).StatusCode); // the other is out
        Assert.Equal(HttpStatusCode.OK, (await f.A.Public.PostAsJsonAsync("/api/account/login", new { phone, password = "NewPass#5678" })).StatusCode);
    }

    [Fact]
    public async Task MyOrders_ListsOnlyOrdersPlacedWhileLoggedIn()
    {
        var mine = await Register(f.A);
        var other = await Register(f.A);
        var (myOrder, _, _) = await OrderAs(f.A, mine.Token);
        await OrderAs(f.A, other.Token);
        await OrderAs(f.A, null);   // guest order — never attributed to anyone

        var list = await (await f.A.Public.SendAsync(Req(HttpMethod.Get, "/api/account/orders", mine.Token))).Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json);
        Assert.Equal(1, list.GetArrayLength());
        Assert.Equal(1, await f.Scalar<long>("SELECT count(*) FROM orders WHERE id=@i AND customer_account_id=@a", ("i", myOrder), ("a", mine.Id)));
    }

    // ── Customer groups & discounts ──────────────────────────────────────────

    [Fact]
    public async Task GroupDiscount_AppliesOnlyToAssignedMembers_AndNeverStacksWithCoupons()
    {
        var admin = await f.A.LoginAsync();
        var gname = "VIP" + Guid.NewGuid().ToString("N")[..6];
        var g = await admin.PostAsJsonAsync("/api/customer-groups", new { name = gname, discountPercent = 10 });
        Assert.Equal(HttpStatusCode.OK, g.StatusCode);
        var groupId = (await Shop.Body(g)).GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/customer-groups", new { name = gname.ToLower(), discountPercent = 5 })).StatusCode); // duplicate (case-insensitive)
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/customer-groups", new { name = "Bad", discountPercent = 101 })).StatusCode);

        var member = await Register(f.A);
        var nonMember = await Register(f.A);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/customer-groups/accounts/{member.Id}/group", new { groupId })).StatusCode);

        // Product price is 1000 => 10% = 100 for the member only.
        var m = await OrderAs(f.A, member.Token);
        Assert.Equal(100m, m.Discount);
        Assert.Equal($"{gname} member 10%", m.Label);
        Assert.Equal(0m, (await OrderAs(f.A, nonMember.Token)).Discount);
        Assert.Equal(0m, (await OrderAs(f.A, null)).Discount);

        // A smaller coupon (5% = 50) loses to the group (100): group applies, coupon is NOT consumed.
        var small = "SM" + Guid.NewGuid().ToString("N")[..6].ToUpper();
        await admin.PostAsJsonAsync("/api/coupons", new { code = small, discountType = "PERCENT", discountValue = 5, minOrderAmount = 0, isActive = true });
        var withSmall = await OrderAs(f.A, member.Token, small);
        Assert.Equal(100m, withSmall.Discount);
        Assert.Equal(0, await f.Scalar<int>("SELECT used_count FROM coupons WHERE code=@c AND company_id=@id", ("c", small), ("id", f.A.CompanyId)));

        // A larger coupon (300 off) beats the group: coupon applies, is consumed, and discounts do not add up.
        var big = "BG" + Guid.NewGuid().ToString("N")[..6].ToUpper();
        await admin.PostAsJsonAsync("/api/coupons", new { code = big, discountType = "FIXED", discountValue = 300, minOrderAmount = 0, isActive = true });
        var withBig = await OrderAs(f.A, member.Token, big);
        Assert.Equal(300m, withBig.Discount);
        Assert.Equal($"Coupon {big}", withBig.Label);
        Assert.Equal(1, await f.Scalar<int>("SELECT used_count FROM coupons WHERE code=@c AND company_id=@id", ("c", big), ("id", f.A.CompanyId)));

        // Removed from the group => no more discount.
        await admin.PutAsJsonAsync($"/api/customer-groups/accounts/{member.Id}/group", new { groupId = (int?)null });
        Assert.Equal(0m, (await OrderAs(f.A, member.Token)).Discount);
    }

    [Fact]
    public async Task CustomerAdmin_IsTenantScoped()
    {
        var a = await f.A.LoginAsync();
        var b = await f.B.LoginAsync();
        var shopperA = await Register(f.A);
        var gA = await Shop.Body(await a.PostAsJsonAsync("/api/customer-groups", new { name = "Iso" + Guid.NewGuid().ToString("N")[..6], discountPercent = 5 }));
        var groupA = gA.GetProperty("id").GetInt32();

        var listB = await b.GetFromJsonAsync<JsonElement>("/api/customer-groups/accounts", ApiFixture.Json);
        Assert.DoesNotContain(listB.EnumerateArray(), x => x.GetProperty("id").GetInt32() == shopperA.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PutAsJsonAsync($"/api/customer-groups/accounts/{shopperA.Id}/group", new { groupId = (int?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PutAsJsonAsync($"/api/customer-groups/accounts/{shopperA.Id}/active", false)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync($"/api/customer-groups/{groupA}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PutAsJsonAsync($"/api/customer-groups/{groupA}", new { name = "x", discountPercent = 1 })).StatusCode);

        // B can't attach its own shopper to A's group either.
        var shopperB = await Register(f.B);
        Assert.Equal(HttpStatusCode.BadRequest, (await b.PutAsJsonAsync($"/api/customer-groups/accounts/{shopperB.Id}/group", new { groupId = groupA })).StatusCode);
    }

    [Fact]
    public async Task DisablingAnAccount_SignsItOutImmediately()
    {
        var admin = await f.A.LoginAsync();
        var s = await Register(f.A);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/customer-groups/accounts/{s.Id}/active", false)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.A.Public.SendAsync(Req(HttpMethod.Get, "/api/account/me", s.Token))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.A.Public.PostAsJsonAsync("/api/account/login", new { phone = s.Phone, password = Pw })).StatusCode);
        await admin.PutAsJsonAsync($"/api/customer-groups/accounts/{s.Id}/active", true);
        Assert.Equal(HttpStatusCode.OK, (await f.A.Public.PostAsJsonAsync("/api/account/login", new { phone = s.Phone, password = Pw })).StatusCode);
    }

    // ── Collections ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Collections_AreTenantScoped_AndOnlyActiveOnesArePublic()
    {
        var a = await f.A.LoginAsync();
        var name = "Eid " + Guid.NewGuid().ToString("N")[..5];
        var key = $"Item {f.A.Slug}::";
        var created = await a.PostAsJsonAsync("/api/collections", new { name, description = "d", isActive = true, sortOrder = 1, productKeys = new[] { key, key, "Other::3" } });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var c = await Shop.Body(created);
        var id = c.GetProperty("id").GetInt32();
        var slug = c.GetProperty("slug").GetString()!;

        var pub = await f.A.Public.GetFromJsonAsync<JsonElement>($"/api/collections/public/{slug}", ApiFixture.Json);
        Assert.Equal(2, pub.GetProperty("productKeys").GetArrayLength());   // duplicates collapsed

        // Another shop sees neither the list entry nor the slug, and can't change or delete it.
        var b = await f.B.LoginAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await f.B.Public.GetAsync($"/api/collections/public/{slug}")).StatusCode);
        var bList = await b.GetFromJsonAsync<JsonElement>("/api/collections", ApiFixture.Json);
        Assert.DoesNotContain(bList.EnumerateArray(), x => x.GetProperty("id").GetInt32() == id);
        Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync($"/api/collections/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PutAsJsonAsync($"/api/collections/{id}", new { name = "x2", isActive = true, sortOrder = 0 })).StatusCode);
        Assert.Equal(0, (await f.NewClient().GetFromJsonAsync<JsonElement>("/api/collections/public", ApiFixture.Json)).GetArrayLength());   // no store header

        // Hidden when inactive; the same name in another shop gets its own slug space.
        await a.PutAsJsonAsync($"/api/collections/{id}", new { name, isActive = false, sortOrder = 1, productKeys = (string[]?)null });
        Assert.Equal(HttpStatusCode.NotFound, (await f.A.Public.GetAsync($"/api/collections/public/{slug}")).StatusCode);
        var again = await a.PostAsJsonAsync("/api/collections", new { name, isActive = true, sortOrder = 2 });
        Assert.NotEqual(slug, (await Shop.Body(again)).GetProperty("slug").GetString());   // same name => suffixed slug
        Assert.Equal(HttpStatusCode.BadRequest, (await a.PostAsJsonAsync("/api/collections", new { name = "", isActive = true, sortOrder = 0 })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await a.DeleteAsync($"/api/collections/{id}")).StatusCode);
    }

    // ── Activity log & sessions ──────────────────────────────────────────────

    [Fact]
    public async Task ActivityLog_RecordsActions_NeverLeaksSecrets_AndIsTenantScoped()
    {
        var a = await f.A.LoginAsync();
        var (orderId, orderNo) = await f.A.PlaceOrder();
        await a.PutAsJsonAsync($"/api/orders/{orderId}/status", new { status = "PROCESSING" });
        var code = "LOG" + Guid.NewGuid().ToString("N")[..6].ToUpper();
        await a.PostAsJsonAsync("/api/coupons", new { code, discountType = "FIXED", discountValue = 10, minOrderAmount = 0, isActive = true });
        await a.PutAsJsonAsync("/api/settings", new[] { new { key = "secret_steadfast_api_key", value = "TOP-SECRET-VALUE-XYZ", groupName = "COURIER" } });

        var log = await a.GetFromJsonAsync<JsonElement>("/api/activity/log", ApiFixture.Json);
        var rows = log.EnumerateArray().ToList();
        Assert.Contains(rows, r => r.GetProperty("action").GetString() == "order.status" && r.GetProperty("details").GetString()!.Contains(orderNo));
        Assert.Contains(rows, r => r.GetProperty("action").GetString() == "coupon.create" && r.GetProperty("details").GetString() == code);
        Assert.Contains(rows, r => r.GetProperty("action").GetString() == "settings.update");
        Assert.DoesNotContain("TOP-SECRET-VALUE-XYZ", log.GetRawText());                      // keys are logged, never values
        Assert.All(rows.Where(r => r.GetProperty("user").ValueKind == JsonValueKind.String), r => Assert.Equal(f.A.Email, r.GetProperty("user").GetString()));

        var filtered = await a.GetFromJsonAsync<JsonElement>("/api/activity/log?action=coupon.", ApiFixture.Json);
        Assert.All(filtered.EnumerateArray(), r => Assert.StartsWith("coupon.", r.GetProperty("action").GetString()));

        var bLog = await (await f.B.LoginAsync()).GetFromJsonAsync<JsonElement>("/api/activity/log", ApiFixture.Json);
        Assert.DoesNotContain(bLog.EnumerateArray(), r => (r.GetProperty("details").GetString() ?? "").Contains(code) || (r.GetProperty("details").GetString() ?? "").Contains(orderNo));
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.NewClient().GetAsync("/api/activity/log")).StatusCode);
    }

    [Fact]
    public async Task StaffSessions_AreTenantScoped_AndYouCannotRevokeYourself()
    {
        var a = await f.A.LoginAsync();
        var list = await a.GetFromJsonAsync<JsonElement>("/api/activity/sessions", ApiFixture.Json);
        Assert.Contains(list.EnumerateArray(), s => s.GetProperty("email").GetString() == f.A.Email);
        Assert.DoesNotContain(list.EnumerateArray(), s => s.GetProperty("email").GetString() == f.B.Email);

        var mine = list.EnumerateArray().First(s => s.GetProperty("email").GetString() == f.A.Email).GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.BadRequest, (await a.DeleteAsync($"/api/activity/sessions/{mine}")).StatusCode);

        await f.B.LoginAsync();
        var bSession = await f.Scalar<int>("SELECT id FROM user_sessions WHERE company_id=@c AND is_active ORDER BY id DESC LIMIT 1", ("c", f.B.CompanyId));
        Assert.Equal(HttpStatusCode.NotFound, (await a.DeleteAsync($"/api/activity/sessions/{bSession}")).StatusCode);
        Assert.True(await f.Scalar<bool>("SELECT is_active FROM user_sessions WHERE id=@i", ("i", bSession)));   // untouched
    }
}
