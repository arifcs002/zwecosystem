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

    [Fact]
    public async Task Upload_IsStoredOnDisk_AndServedFromThere()
    {
        using var img = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(20, 20);
        using var ms = new MemoryStream();
        await SixLabors.ImageSharp.ImageExtensions.SaveAsPngAsync(img, ms);
        var form = new MultipartFormDataContent { { new ByteArrayContent(ms.ToArray()), "file", "a.png" } };
        var r = await f.A.Admin.PostAsync("/api/upload?folder=product", form);
        await Shop.Ok(r, "upload");
        var url = (await Shop.Body(r)).GetProperty("imageUrl").GetString()!;
        var name = url.Split('/').Last();

        var path = Ecommerce.Api.Infrastructure.ImageStore.PathFor("product", name)!;
        Assert.True(File.Exists(path));

        var g = await f.NewClient().GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, g.StatusCode);
        Assert.Equal(await File.ReadAllBytesAsync(path), await g.Content.ReadAsByteArrayAsync());

        // Legacy path: only in disk-less state → still 404 for unknown / traversal names.
        Assert.Equal(HttpStatusCode.NotFound, (await f.NewClient().GetAsync("/uploads/product/0000000000000000.jpg")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await f.NewClient().GetAsync("/uploads/secrets/" + name)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await f.NewClient().GetAsync("/uploads/product/..%2f..%2fappsettings.json")).StatusCode);
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
