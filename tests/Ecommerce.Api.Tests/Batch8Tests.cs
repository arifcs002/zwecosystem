using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using Ecommerce.Api.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Ecommerce.Api.Tests;

[Collection("api")]
public class Batch8Tests(ApiFixture f)
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private void Forget(Shop s) =>
        PlanAccess.Invalidate(f.Factory.Services.GetRequiredService<IMemoryCache>(), s.CompanyId);

    /// <summary>Puts a shop on a brand-new private plan with the given features and expiry (shared plans are never edited).</summary>
    private async Task<int> Put(Shop s, string featuresJson, DateTime? expires)
    {
        var name = "T-Plan-" + Guid.NewGuid().ToString("N")[..8];
        await f.Exec("INSERT INTO subscription_plans (name, price, billing_cycle, features, created_date, updated_date, is_deleted) VALUES (@n, 100, 'monthly', @f::jsonb, now(), now(), 0)",
            ("n", name), ("f", featuresJson));
        var planId = await f.Scalar<int>("SELECT id FROM subscription_plans WHERE name=@n", ("n", name));
        await f.Exec("UPDATE companies SET subscription_plan_id=@p, subscription_expires_at=@e WHERE id=@c",
            ("p", planId), ("e", (object?)expires ?? DBNull.Value), ("c", s.CompanyId));
        Forget(s);
        return planId;
    }

    /// <summary>Puts the shop back on the shared Basic plan with no end date, and removes the private test plans.</summary>
    private async Task Reset(Shop s)
    {
        await f.Exec("UPDATE companies SET subscription_plan_id=(SELECT id FROM subscription_plans WHERE name='Basic Plan'), subscription_expires_at=NULL WHERE id=@c", ("c", s.CompanyId));
        await f.Exec("DELETE FROM subscription_plans WHERE name LIKE 'T-Plan-%' AND id NOT IN (SELECT subscription_plan_id FROM companies WHERE subscription_plan_id IS NOT NULL)");
        Forget(s);
    }

    private async Task<HttpClient> SuperAdmin()
    {
        var c = f.NewClient();
        var r = await c.PostAsJsonAsync("/api/auth/login", new { email = "arifsuperadmin", password = "123456" });
        await Shop.Ok(r, "superadmin login");
        var token = (await Shop.Body(r)).GetProperty("sessionToken").GetString()!;
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    private static string Cat(int c) => c.ToString();
    private async Task<int> CatId(Shop s) => await f.Scalar<int>("SELECT id FROM categories WHERE company_id=@c ORDER BY id LIMIT 1", ("c", s.CompanyId));

    private async Task<HttpResponseMessage> AddProduct(Shop s, string name, int sizes = 1, int firstSize = 0) =>
        await s.Admin.PostAsJsonAsync("/api/products/batch", new
        {
            name, price = 100, wholesalePrice = 50, categoryId = await CatId(s),
            sizes = Enumerable.Range(firstSize, sizes).Select(i => new { size = "S" + i, quantity = 2 }).ToArray()
        });

    // ── module gating ────────────────────────────────────────────────────────

    [Fact]
    public async Task PosDisabled_BlocksPos_ButNotTheRestOfTheShop()
    {
        var s = f.A; await s.LoginAsync();
        try
        {
            await Put(s, "{\"pos_enabled\": false}", null);
            var pos = await s.Admin.GetAsync("/api/pos/lookup?barcode=x");
            Assert.Equal(HttpStatusCode.Forbidden, pos.StatusCode);
            Assert.Equal("module_disabled", (await Shop.Body(pos)).GetProperty("code").GetString());
            Assert.Equal(HttpStatusCode.OK, (await s.Admin.GetAsync("/api/products")).StatusCode);

            await Put(s, "{\"ecommerce_enabled\": true}", null);           // flag absent for POS → allowed
            Assert.NotEqual(HttpStatusCode.Forbidden, (await s.Admin.GetAsync("/api/pos/lookup?barcode=x")).StatusCode);
        }
        finally { await Reset(s); }
    }

    [Fact]
    public async Task EcommerceDisabled_BlocksThatShopsStorefront_NotAnotherShops()
    {
        var s = f.A;
        try
        {
            await Put(s, "{\"ecommerce_enabled\": false}", null);
            Assert.Equal(HttpStatusCode.Forbidden, (await s.Public.GetAsync($"/api/storefront/{s.Slug}")).StatusCode);
            var order = await s.Public.PostAsJsonAsync("/api/orders/public", new
            { customerName = "B", customerPhone = "01711111111", shippingAddress = "House 1, Road 2, Dhaka", paymentMethod = "COD", items = Array.Empty<object>() });
            Assert.Equal(HttpStatusCode.Forbidden, order.StatusCode);

            Assert.Equal(HttpStatusCode.OK, (await f.B.Public.GetAsync($"/api/storefront/{f.B.Slug}")).StatusCode);
        }
        finally { await Reset(s); }
    }

    // ── limits ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task MaxProducts_CountsProductsNotSizes_AndAllowsRestockOfExistingOnes()
    {
        var s = f.A; await s.LoginAsync();
        try
        {
            var existing = await f.Scalar<int>("SELECT COUNT(*)::int FROM (SELECT DISTINCT regexp_replace(name, '\\s*\\(Size [^)]+\\)\\s*$', ''), category_id FROM products WHERE company_id=@c AND is_deleted=0) t", ("c", s.CompanyId));
            await Put(s, "{\"max_products\": " + (existing + 1) + "}", null);

            var tag = Guid.NewGuid().ToString("N")[..6];
            Assert.Equal(HttpStatusCode.OK, (await AddProduct(s, "Limit " + tag, sizes: 3)).StatusCode);   // 3 size rows = ONE product
            var over = await AddProduct(s, "Limit2 " + tag);
            Assert.Equal(HttpStatusCode.Forbidden, over.StatusCode);
            Assert.Equal("plan_limit", (await Shop.Body(over)).GetProperty("code").GetString());
            Assert.Equal(HttpStatusCode.OK, (await AddProduct(s, "Limit " + tag, firstSize: 9)).StatusCode);   // another size of the same product is not "new"
        }
        finally { await Reset(s); }
    }

    [Fact]
    public async Task SingleUserPlan_CannotAddStaff()
    {
        var s = f.A; var admin = await s.LoginAsync();
        try
        {
            await Put(s, "{\"multi_staff\": false}", null);
            var r = await admin.PostAsJsonAsync("/api/users", new
            { companyId = s.CompanyId, email = $"staff{Guid.NewGuid():N}@example.test", password = "Test@12345", firstName = "S", lastName = "T", phoneNumber = "01700000000", role = "salesstaff", isActive = true });
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
            Assert.Equal("plan_limit", (await Shop.Body(r)).GetProperty("code").GetString());
        }
        finally { await Reset(s); }
    }

    // ── expiry ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Expired_BlocksStaffWrites_ButNotReadsBillingOrTheStorefront_AndGraceStillWorks()
    {
        var s = f.A; var admin = await s.LoginAsync();
        try
        {
            await Put(s, "{}", DateTime.UtcNow.AddDays(-3));                         // inside the 7-day grace
            Assert.Equal(HttpStatusCode.OK, (await AddProduct(s, "Grace " + Guid.NewGuid().ToString("N")[..6])).StatusCode);
            Assert.Equal("grace", (await Shop.Body(await admin.GetAsync("/api/billing/my"))).GetProperty("status").GetString());

            await Put(s, "{}", DateTime.UtcNow.AddDays(-10));                        // past grace
            var blocked = await AddProduct(s, "Late " + Guid.NewGuid().ToString("N")[..6]);
            Assert.Equal(HttpStatusCode.PaymentRequired, blocked.StatusCode);
            Assert.Equal("plan_expired", (await Shop.Body(blocked)).GetProperty("code").GetString());

            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/products")).StatusCode);                 // reads still work
            Assert.Equal("expired", (await Shop.Body(await admin.GetAsync("/api/billing/my"))).GetProperty("status").GetString());
            Assert.Equal(HttpStatusCode.OK, (await s.Public.GetAsync($"/api/storefront/{s.Slug}")).StatusCode);   // shoppers unaffected
            var place = await s.PlaceOrder(phone: "01711999999");                                                 // customers can still order
            Assert.True(place.Id > 0);
        }
        finally { await Reset(s); }
    }

    // ── payments ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task RecordingAPayment_ExtendsTheSubscription_AndOnlyThePlatformOwnerCan()
    {
        var s = f.A; var admin = await s.LoginAsync();
        var sa = await SuperAdmin();
        try
        {
            await Put(s, "{}", DateTime.UtcNow.AddDays(-20));                        // lapsed → paying starts from today
            // The shop itself can't record a payment, and can't see/record another shop's.
            Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync($"/api/billing/companies/{s.CompanyId}/payments", new { amount = 100, method = "cash", months = 1 })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync($"/api/billing/companies/{f.B.CompanyId}")).StatusCode);

            Assert.Equal(HttpStatusCode.BadRequest, (await sa.PostAsJsonAsync($"/api/billing/companies/{s.CompanyId}/payments", new { amount = 100, method = "paypal", months = 1 })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await sa.PostAsJsonAsync($"/api/billing/companies/{s.CompanyId}/payments", new { amount = 100, method = "cash", months = 0 })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await sa.PostAsJsonAsync($"/api/billing/companies/{s.CompanyId}/payments", new { amount = -5, method = "cash", months = 1 })).StatusCode);

            var r = await sa.PostAsJsonAsync($"/api/billing/companies/{s.CompanyId}/payments", new { amount = 1500, method = "bkash", reference = "TXN123", months = 2 });
            await Shop.Ok(r, "payment");
            var sum = await Shop.Body(r);
            Assert.Equal("active", sum.GetProperty("status").GetString());
            var end = sum.GetProperty("expiresAt").GetDateTime();
            Assert.InRange((end - DateTime.UtcNow).TotalDays, 58, 62);              // ≈ 2 months from today

            // Paying again while active extends from the current end date, not from today.
            var r2 = await sa.PostAsJsonAsync($"/api/billing/companies/{s.CompanyId}/payments", new { amount = 750, method = "cash", months = 1 });
            var end2 = (await Shop.Body(r2)).GetProperty("expiresAt").GetDateTime();
            Assert.InRange((end2 - end).TotalDays, 27, 32);

            // The shop sees its own history, and writes work again.
            var mine = await Shop.Body(await admin.GetAsync("/api/billing/my"));
            Assert.Equal(2, mine.GetProperty("payments").GetArrayLength());
            Assert.Equal("TXN123", mine.GetProperty("payments").EnumerateArray().Last().GetProperty("reference").GetString());
            Assert.NotEqual(HttpStatusCode.PaymentRequired, (await AddProduct(s, "Paid " + Guid.NewGuid().ToString("N")[..6])).StatusCode);

            // Shop B never sees shop A's payments.
            var b = await f.B.LoginAsync();
            Assert.Equal(0, (await Shop.Body(await b.GetAsync("/api/billing/my"))).GetProperty("payments").GetArrayLength());
        }
        finally { await Reset(s); await f.Exec("DELETE FROM subscription_payments WHERE company_id=@c", ("c", s.CompanyId)); }
    }

    // ── signup ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Signup_PicksThePlan_StartsATrial_AndRejectsUnknownPlans()
    {
        var planId = await f.Scalar<int>("SELECT id FROM subscription_plans WHERE name='Premium Plan'");
        var slug = "sg" + Guid.NewGuid().ToString("N")[..8];
        object Body(int plan, string sub) => new
        {
            companyName = "Signup " + sub, subdomain = sub, ownerFirstName = "S", ownerLastName = "O", ownerEmail = $"{sub}@example.test",
            ownerPhone = "01700000000", password = "Test@12345", address = "x", division = "x", district = "x", thana = "x", planId = plan
        };

        var bad = await f.NewClient().PostAsJsonAsync("/api/auth/register-company", Body(999999, slug));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var ok = await f.NewClient().PostAsJsonAsync("/api/auth/register-company", Body(planId, slug));
        await Shop.Ok(ok, "register");
        var cid = (await Shop.Body(ok)).GetProperty("companyId").GetInt32();
        Assert.Equal(planId, await f.Scalar<int>("SELECT subscription_plan_id FROM companies WHERE id=@c", ("c", cid)));
        var days = (await f.Scalar<DateTime>("SELECT subscription_expires_at FROM companies WHERE id=@c", ("c", cid)) - DateTime.UtcNow).TotalDays;
        Assert.InRange(days, 13, 15);
    }
}

[Collection("api")]
public class Batch8PublicPlanTests(ApiFixture f)
{
    [Fact]
    public async Task PublicPlanList_NeedsNoLogin_AndExposesOnlyPriceListFields()
    {
        var r = await f.NewClient().GetAsync("/api/subscriptionplans/public");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var rows = (await Shop.Body(r)).EnumerateArray().ToList();
        Assert.NotEmpty(rows);
        var names = rows[0].EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "billingCycle", "features", "id", "name", "price", "trialDays" }, names);
        // The management list still needs a login.
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.NewClient().GetAsync("/api/subscriptionplans")).StatusCode);
    }
}
