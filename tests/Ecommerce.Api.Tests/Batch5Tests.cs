using Microsoft.Extensions.DependencyInjection;
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
    public async Task SuperAdminPassword_RotatesOnlySeedHash_AndEnforcesMinLength()
    {
        var seed = Ecommerce.Api.Infrastructure.SuperAdminBootstrap.SeedHash;
        var logs = new List<string>();
        async Task<string> Hash() => await f.Scalar<string>("SELECT password_hash FROM users WHERE email='arifsuperadmin'");
        void Run(string? pw)
        {
            using var scope = f.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Ecommerce.Api.Infrastructure.ApplicationDbContext>();
            Ecommerce.Api.Infrastructure.SuperAdminBootstrap.Run(db, pw, true, logs.Add);
        }
        try
        {
            await f.Exec("UPDATE users SET password_hash=@h WHERE email='arifsuperadmin'", ("h", seed));

            Run("short");                                   // too short → untouched
            Assert.Equal(seed, await Hash());
            Run(null);                                      // no env → warning only
            Assert.Contains(logs, l => l.Contains("WARNING"));
            Assert.Equal(seed, await Hash());

            Run("a-Strong-New-Password-1");                 // rotated
            Assert.True(BCrypt.Net.BCrypt.Verify("a-Strong-New-Password-1", await Hash()));
            Assert.False(BCrypt.Net.BCrypt.Verify("123456", await Hash()));

            Run("A-Different-Password-22");                 // already changed → NOT overwritten
            Assert.True(BCrypt.Net.BCrypt.Verify("a-Strong-New-Password-1", await Hash()));
        }
        finally { await f.Exec("UPDATE users SET password_hash=@h WHERE user_type='superadmin'", ("h", seed)); }
    }

    [Fact]
    public async Task Upload_IsStoredOnDisk_AndServedFromThere()
    {
        using var bmp = new SkiaSharp.SKBitmap(20, 20);
        using var skImage = SkiaSharp.SKImage.FromBitmap(bmp);
        using var png = skImage.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        using var ms = new MemoryStream(png.ToArray());
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
