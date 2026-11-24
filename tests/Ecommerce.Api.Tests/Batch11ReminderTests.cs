using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ecommerce.Api.Tests;

[Collection("api")]
public class Batch11ReminderTests(ApiFixture f)
{
    private static string Phone() => "017" + Random.Shared.Next(10000000, 99999999);

    private Task SetSetting(Shop s, string key, string? value) => value == null
        ? f.Exec("DELETE FROM company_settings WHERE company_id=@c AND key=@k", ("c", s.CompanyId), ("k", key))
        : f.Exec("INSERT INTO company_settings (company_id,key,value,group_name,created_date,updated_date,is_deleted) VALUES (@c,@k,@v,'SMS',NOW(),NOW(),0)", ("c", s.CompanyId), ("k", key), ("v", value));

    private Task<HttpResponseMessage> CreditSale(HttpClient c, int productId, string phone, string name, int qty = 1) =>
        c.PostAsJsonAsync("/api/pos/checkout", new { items = new[] { new { productId, quantity = qty, price = (decimal?)null } }, discount = 0, paymentMethod = "CREDIT", customerName = name, customerPhone = phone });

    private int SmsCount() { lock (f.Sms.Sent) return f.Sms.Sent.Count; }
    private List<(string? Url, string To, string Message)> SmsTo(string phone) { lock (f.Sms.Sent) return f.Sms.Sent.Where(s => s.To == phone).ToList(); }

    [Fact]
    public async Task WithoutAGateway_YouStillGetAWhatsAppLink_WithTheRightNumberAndAmount()
    {
        var a = await f.A.LoginAsync();
        await SetSetting(f.A, "sms_api_url", null);
        var phone = Phone();
        await Shop.Ok(await CreditSale(a, f.A.ProductId, phone, "Karim Uddin", qty: 2), "credit sale");     // owes 2000
        var before = SmsCount();

        var r = await Shop.Body(await a.PostAsJsonAsync("/api/ledger/remind", new { phone }));
        Assert.False(r.GetProperty("sent").GetBoolean());
        Assert.Equal("no_sms_gateway", r.GetProperty("skipped").GetString());
        Assert.Equal(before, SmsCount());                                                                  // nothing was texted
        var link = r.GetProperty("whatsappUrl").GetString()!;
        Assert.StartsWith($"https://wa.me/88{phone}?text=", link);                                         // international format, no leading 0
        var text = Uri.UnescapeDataString(link[(link.IndexOf("text=") + 5)..]);
        Assert.Contains("Karim Uddin", text);
        Assert.Contains("2000", text);
    }

    [Fact]
    public async Task WithAGateway_TheSmsIsSent_AndANewReminderWithinSixHoursIsHeldBack()
    {
        var a = await f.A.LoginAsync();
        await SetSetting(f.A, "sms_api_url", "http://gw.test/?to={to}&m={msg}");
        try
        {
            var phone = Phone();
            await Shop.Ok(await CreditSale(a, f.A.ProductId, phone, "Nasima", qty: 1), "credit sale");
            var first = await Shop.Body(await a.PostAsJsonAsync("/api/ledger/remind", new { phone }));
            Assert.True(first.GetProperty("sent").GetBoolean());
            var sms = Assert.Single(SmsTo(phone));
            Assert.Contains("Nasima", sms.Message);
            Assert.Contains("1000", sms.Message);

            var again = await Shop.Body(await a.PostAsJsonAsync("/api/ledger/remind", new { phone }));
            Assert.False(again.GetProperty("sent").GetBoolean());
            Assert.StartsWith("reminded_", again.GetProperty("skipped").GetString());
            Assert.Single(SmsTo(phone));                                                                   // no second text
            Assert.Contains("wa.me", again.GetProperty("whatsappUrl").GetString());                        // WhatsApp link still offered
        }
        finally { await SetSetting(f.A, "sms_api_url", null); }
    }

    [Fact]
    public async Task OnlyPeopleWhoOwe_CanBeReminded()
    {
        var a = await f.A.LoginAsync();
        var phone = Phone();
        Assert.Equal(HttpStatusCode.NotFound, (await a.PostAsJsonAsync("/api/ledger/remind", new { phone })).StatusCode);          // no account
        Assert.Equal(HttpStatusCode.BadRequest, (await a.PostAsJsonAsync("/api/ledger/remind", new { phone = "12345" })).StatusCode);
        await Shop.Ok(await CreditSale(a, f.A.ProductId, phone, "Paid Up"), "sale");
        await Shop.Ok(await a.PostAsJsonAsync("/api/ledger/payments", new { phone, amount = 1000, method = "cash" }), "settle");
        var settled = await a.PostAsJsonAsync("/api/ledger/remind", new { phone });
        Assert.Equal(HttpStatusCode.BadRequest, settled.StatusCode);
        Assert.Contains("doesn't owe", (await Shop.Body(settled)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task TheShopCanWriteItsOwnWording()
    {
        var a = await f.A.LoginAsync();
        await SetSetting(f.A, "sms_api_url", "http://gw.test/?to={to}&m={msg}");
        await SetSetting(f.A, "due_reminder_template", "Hi {name}! ৳{due} is pending at {shop}.");
        try
        {
            var phone = Phone();
            await Shop.Ok(await CreditSale(a, f.A.ProductId, phone, "Rubel"), "sale");
            await Shop.Ok(await a.PostAsJsonAsync("/api/ledger/remind", new { phone }), "remind");
            var msg = Assert.Single(SmsTo(phone)).Message;
            Assert.StartsWith("Hi Rubel! ৳1000 is pending at ", msg);
            Assert.DoesNotContain("{", msg);                                                               // every placeholder was filled
        }
        finally { await SetSetting(f.A, "sms_api_url", null); await SetSetting(f.A, "due_reminder_template", null); }
    }

    [Fact]
    public async Task RemindAll_TextsEveryoneAboveTheLimit_SkipsSmallAndRecentOnes_AndIsOwnerOnly()
    {
        var b = await f.B.LoginAsync();
        var big1 = Phone(); var big2 = Phone(); var small = Phone();
        await Shop.Ok(await CreditSale(b, f.B.ProductId, big1, "Big One"), "b1");                         // owes 1000
        await Shop.Ok(await CreditSale(b, f.B.ProductId, big2, "Big Two"), "b2");
        await Shop.Ok(await b.PostAsJsonAsync("/api/pos/checkout", new { items = new[] { new { productId = f.B.ProductId, quantity = 1, price = (decimal?)null } }, discount = 0, paymentMethod = "CREDIT", customerName = "Small", customerPhone = small, amountPaid = 995m }), "small: owes 5");

        // not set up yet
        await SetSetting(f.B, "sms_api_url", null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await b.PostAsJsonAsync("/api/ledger/remind-all", new { minDue = 100 })).StatusCode);

        await SetSetting(f.B, "sms_api_url", "http://gw.test/?to={to}&m={msg}");
        try
        {
            // a staff member (not the owner) must not be able to spend SMS money
            var staffEmail = $"staff{Guid.NewGuid():N}@example.test";
            await Shop.Ok(await b.PostAsJsonAsync("/api/users", new { companyId = f.B.CompanyId, email = staffEmail, password = "Test@12345", firstName = "S", lastName = "T", phoneNumber = "01700000000", role = "salesstaff", isActive = true }), "staff user");
            var staff = f.NewClient();
            var login = await staff.PostAsJsonAsync("/api/auth/login", new { email = staffEmail, password = "Test@12345", loginContext = f.B.Slug });
            await Shop.Ok(login, "staff login");
            staff.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await Shop.Body(login)).GetProperty("sessionToken").GetString()!);
            Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync("/api/ledger/remind-all", new { minDue = 100 })).StatusCode);
            Assert.Empty(SmsTo(big1));

            var r = await Shop.Body(await b.PostAsJsonAsync("/api/ledger/remind-all", new { minDue = 100 }));
            Assert.True(r.GetProperty("sent").GetInt32() >= 2);
            Assert.Single(SmsTo(big1)); Assert.Single(SmsTo(big2));
            Assert.Empty(SmsTo(small));                                                                    // owes only ৳5: below the limit

            var second = await Shop.Body(await b.PostAsJsonAsync("/api/ledger/remind-all", new { minDue = 100 }));
            Assert.Equal(0, second.GetProperty("sent").GetInt32());                                        // all reminded within 6 hours
            Assert.True(second.GetProperty("tooSoon").GetInt32() >= 2);
            Assert.Single(SmsTo(big1));
        }
        finally { await SetSetting(f.B, "sms_api_url", null); }
    }

    [Fact]
    public async Task OneShopCannotRemindAnotherShopsCustomers()
    {
        var a = await f.A.LoginAsync(); var b = await f.B.LoginAsync();
        var phone = Phone();
        await Shop.Ok(await CreditSale(a, f.A.ProductId, phone, "A's customer"), "sale");
        await SetSetting(f.B, "sms_api_url", "http://gw.test/?to={to}&m={msg}");
        try
        {
            Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsJsonAsync("/api/ledger/remind", new { phone })).StatusCode);
            await b.PostAsJsonAsync("/api/ledger/remind-all", new { minDue = 1 });
            Assert.Empty(SmsTo(phone));                                                                    // B's bulk send never reaches A's customer
            Assert.Equal(HttpStatusCode.Unauthorized, (await f.NewClient().PostAsJsonAsync("/api/ledger/remind", new { phone })).StatusCode);
        }
        finally { await SetSetting(f.B, "sms_api_url", null); }
    }
}
