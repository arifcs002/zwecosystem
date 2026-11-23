using System.Net;

namespace Ecommerce.Api.Tests;

[Collection("api")]
public class Batch5Tests(ApiFixture f)
{
    private async Task<string?> AllowOrigin(string origin)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/seo/robots?host=x.localhost&path=/&scheme=https");
        req.Headers.Add("Origin", origin);
        var r = await f.NewClient().SendAsync(req);
        return r.Headers.TryGetValues("Access-Control-Allow-Origin", out var v) ? v.First() : null;
    }

    [Theory]
    [InlineData("https://auleco.com")]
    [InlineData("https://zairasworld.auleco.com")]
    [InlineData("http://zairasworld.localhost:3800")]
    [InlineData("capacitor://localhost")]
    public async Task Cors_AllowsOurOwnOrigins(string origin) => Assert.Equal(origin, await AllowOrigin(origin));

    [Theory]
    [InlineData("https://evil.com")]
    [InlineData("https://auleco.com.evil.com")]
    [InlineData("https://notauleco.com")]
    public async Task Cors_RejectsOtherOrigins(string origin) => Assert.Null(await AllowOrigin(origin));
}
