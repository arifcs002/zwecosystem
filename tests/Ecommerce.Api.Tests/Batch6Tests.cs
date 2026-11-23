using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Ecommerce.Api.Tests;

[Collection("api")]
public class Batch6Tests(ApiFixture f)
{
    private async Task EnableSms(Shop s) =>
        await f.Exec("INSERT INTO company_settings (company_id,key,value,group_name,created_date,updated_date,is_deleted) VALUES (@c,'sms_api_url','http://gw.test/?to={to}&m={msg}','SMS',NOW(),NOW(),0) ON CONFLICT DO NOTHING", ("c", s.CompanyId));

    private static string NewPhone() => "019" + Random.Shared.Next(10000000, 99999999);

    private async Task<(string Phone, string Token)> Register(Shop s, string pw = "Passw0rd!x")
    {
        var phone = NewPhone();
        var r = await s.Public.PostAsJsonAsync("/api/account/register", new { name = "Buyer", phone, password = pw });
        await Shop.Ok(r, "register");
        return (phone, (await Shop.Body(r)).GetProperty("token").GetString()!);
    }

    private Task<HttpResponseMessage> Send(Shop s, string phone, string purpose = "reset") =>
        s.Public.PostAsJsonAsync("/api/account/otp/send", new { phone, purpose });
    private Task<HttpResponseMessage> Reset(Shop s, string phone, string code, string pw) =>
        s.Public.PostAsJsonAsync("/api/account/password/reset", new { phone, code, newPassword = pw });

    [Fact]
    public async Task NoGateway_MeansNoCodes()
    {
        var s = f.B;
        await f.Exec("DELETE FROM company_settings WHERE company_id=@c AND key='sms_api_url'", ("c", s.CompanyId));
        var (phone, _) = await Register(s);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Send(s, phone)).StatusCode);
    }

    [Fact]
    public async Task PasswordReset_WithCode_ChangesPassword_AndSignsEveryoneOut()
    {
        var s = f.A; await EnableSms(s);
        var (phone, token) = await Register(s);
        Assert.Equal(HttpStatusCode.OK, (await Send(s, phone)).StatusCode);
        var code = f.Sms.LastCodeFor(phone);
        Assert.Matches(@"^\d{6}$", code);

        Assert.Equal(HttpStatusCode.BadRequest, (await Reset(s, phone, "000000" == code ? "111111" : "000000", "NewPassw0rd!")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Reset(s, phone, code, "short")).StatusCode);     // weak password doesn't burn the code
        Assert.Equal(HttpStatusCode.OK, (await Reset(s, phone, code, "NewPassw0rd!")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Reset(s, phone, code, "Another1!xx")).StatusCode); // one use only

        var old = new HttpRequestMessage(HttpMethod.Get, "/api/account/me"); old.Headers.Add("X-Customer-Token", token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Public.SendAsync(old)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Public.PostAsJsonAsync("/api/account/login", new { phone, password = "Passw0rd!x" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await s.Public.PostAsJsonAsync("/api/account/login", new { phone, password = "NewPassw0rd!" })).StatusCode);
    }

    [Fact]
    public async Task UnknownNumber_LooksIdentical_AndSendsNothing()
    {
        var s = f.A; await EnableSms(s);
        var before = f.Sms.Sent.Count;
        var r = await Send(s, NewPhone());
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(before, f.Sms.Sent.Count);
    }

    [Fact]
    public async Task WrongCodes_AreLimited_EvenWithTheRightCodeAfterwards()
    {
        var s = f.A; await EnableSms(s);
        var (phone, _) = await Register(s);
        await Send(s, phone);
        var code = f.Sms.LastCodeFor(phone);
        var wrong = code == "123456" ? "654321" : "123456";
        for (int i = 0; i < 5; i++) await Reset(s, phone, wrong, "NewPassw0rd!");
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Reset(s, phone, code, "NewPassw0rd!")).StatusCode);
    }

    [Fact]
    public async Task CodeFromOneShop_DoesNotWorkInAnother()
    {
        await EnableSms(f.A);
        var (phone, _) = await Register(f.A);
        await Send(f.A, phone);
        var code = f.Sms.LastCodeFor(phone);
        Assert.Equal(HttpStatusCode.BadRequest, (await Reset(f.B, phone, code, "NewPassw0rd!")).StatusCode);
    }

    [Fact]
    public async Task ResendingCancelsTheOlderCode()
    {
        var s = f.A; await EnableSms(s);
        var (phone, _) = await Register(s);
        await Send(s, phone); var first = f.Sms.LastCodeFor(phone);
        await Send(s, phone); var second = f.Sms.LastCodeFor(phone);
        if (first != second) Assert.Equal(HttpStatusCode.BadRequest, (await Reset(s, phone, first, "NewPassw0rd!")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Reset(s, phone, second, "NewPassw0rd!")).StatusCode);
    }

    [Fact]
    public async Task PhoneVerification_MarksAccountVerified()
    {
        var s = f.A; await EnableSms(s);
        var (phone, token) = await Register(s);
        HttpRequestMessage Req(string url, object body) { var m = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) }; m.Headers.Add("X-Customer-Token", token); return m; }

        Assert.Equal(HttpStatusCode.OK, (await s.Public.SendAsync(Req("/api/account/otp/send", new { phone = "", purpose = "verify" }))).StatusCode);
        var code = f.Sms.LastCodeFor(phone);
        var r = await s.Public.SendAsync(Req("/api/account/phone/verify", new { code }));
        await Shop.Ok(r, "verify");
        Assert.True((await Shop.Body(r)).GetProperty("phoneVerified").GetBoolean());
    }
}
