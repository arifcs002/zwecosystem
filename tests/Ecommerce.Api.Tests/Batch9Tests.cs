using System.Net;

namespace Ecommerce.Api.Tests;

[Collection("api")]
public class Batch9Tests(ApiFixture f)
{
    [Theory]
    [InlineData("/api/subscriptionplans/public", HttpStatusCode.OK)]      // a normal JSON response
    [InlineData("/api/subscriptionplans", HttpStatusCode.Unauthorized)]   // an error response gets them too
    public async Task EveryApiResponse_CarriesBaselineSecurityHeaders(string path, HttpStatusCode expected)
    {
        var r = await f.NewClient().GetAsync(path);
        Assert.Equal(expected, r.StatusCode);
        Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", r.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("strict-origin-when-cross-origin", r.Headers.GetValues("Referrer-Policy").Single());
    }
}
