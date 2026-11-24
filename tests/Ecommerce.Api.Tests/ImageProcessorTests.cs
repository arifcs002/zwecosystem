using System.Net;
using System.Net.Http.Headers;
using SkiaSharp;
using Ecommerce.Api.Infrastructure;

namespace Ecommerce.Api.Tests;

public class ImageProcessorTests
{
    private static byte[] Png(int w, int h, SKColor fill, bool alpha = false)
    {
        using var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, alpha ? SKAlphaType.Premul : SKAlphaType.Opaque));
        using (var c = new SKCanvas(bmp)) c.Clear(fill);
        using var img = SKImage.FromBitmap(bmp);
        return img.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    private static SKBitmap Decode(byte[] bytes) => SKBitmap.Decode(bytes);

    [Fact]
    public void Big_pictures_are_shrunk_to_1200_and_small_ones_keep_their_size()
    {
        var big = ImageProcessor.ToJpeg(new MemoryStream(Png(3000, 1500, SKColors.Red)))!;
        using (var b = Decode(big)) { Assert.Equal(1200, b.Width); Assert.Equal(600, b.Height); }
        var small = ImageProcessor.ToJpeg(new MemoryStream(Png(300, 200, SKColors.Red)))!;
        using (var s = Decode(small)) { Assert.Equal(300, s.Width); Assert.Equal(200, s.Height); }
        Assert.Equal(0xFF, big[0]); Assert.Equal(0xD8, big[1]);                    // it really is a JPEG
    }

    [Fact]
    public void Transparent_pictures_get_a_white_background()
    {
        var jpeg = ImageProcessor.ToJpeg(new MemoryStream(Png(40, 40, SKColors.Transparent, alpha: true)))!;
        using var b = Decode(jpeg);
        var p = b.GetPixel(20, 20);
        Assert.True(p.Red > 240 && p.Green > 240 && p.Blue > 240, $"expected white, got {p}");
    }

    [Theory]
    [InlineData("<html><script>alert(1)</script></html>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>")]
    [InlineData("MZ this is not a picture")]
    [InlineData("")]
    public void Anything_that_is_not_a_picture_is_refused(string text)
    {
        Assert.Null(ImageProcessor.ToJpeg(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text))));
    }

    [Fact]
    public void A_truncated_picture_never_throws()
    {
        var png = Png(200, 200, SKColors.Blue);
        var cut = png.Take(png.Length / 3).ToArray();
        var r = Record.Exception(() => ImageProcessor.ToJpeg(new MemoryStream(cut)));
        Assert.Null(r);
    }

    [Fact]
    public void Phone_photo_orientation_is_applied_to_the_pixels()
    {
        // 2x1 picture: left pixel red, right pixel blue
        using var src = new SKBitmap(new SKImageInfo(2, 1, SKColorType.Rgba8888, SKAlphaType.Opaque));
        src.SetPixel(0, 0, SKColors.Red); src.SetPixel(1, 0, SKColors.Blue);

        using var cw = ImageProcessor.Orient(src, SKEncodedOrigin.RightTop);       // EXIF 6: turn 90 degrees clockwise
        Assert.Equal((1, 2), (cw.Width, cw.Height));
        Assert.Equal(SKColors.Red, cw.GetPixel(0, 0)); Assert.Equal(SKColors.Blue, cw.GetPixel(0, 1));

        using var half = ImageProcessor.Orient(src, SKEncodedOrigin.BottomRight);  // EXIF 3: upside down
        Assert.Equal(SKColors.Blue, half.GetPixel(0, 0)); Assert.Equal(SKColors.Red, half.GetPixel(1, 0));

        using var mirror = ImageProcessor.Orient(src, SKEncodedOrigin.TopRight);   // EXIF 2: mirrored
        Assert.Equal(SKColors.Blue, mirror.GetPixel(0, 0)); Assert.Equal(SKColors.Red, mirror.GetPixel(1, 0));

        using var same = ImageProcessor.Orient(src, SKEncodedOrigin.TopLeft);
        Assert.Equal(SKColors.Red, same.GetPixel(0, 0));
    }
}

[Collection("api")]
public class UploadEndpointTests(ApiFixture f)
{
    [Fact]
    public async Task Upload_rejects_non_images_with_400_and_stores_resized_jpegs()
    {
        var a = f.A.Admin ?? await f.A.LoginAsync();
        var html = new MultipartFormDataContent { { new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>")), "file", "x.html" } };
        Assert.Equal(HttpStatusCode.BadRequest, (await a.PostAsync("/api/upload?folder=product", html)).StatusCode);

        using var bmp = new SKBitmap(2400, 1600);
        using (var c = new SKCanvas(bmp)) c.Clear(SKColors.Green);
        using var img = SKImage.FromBitmap(bmp);
        var form = new MultipartFormDataContent { { new ByteArrayContent(img.Encode(SKEncodedImageFormat.Png, 100).ToArray()), "file", "big.png" } };
        var r = await a.PostAsync("/api/upload?folder=product", form);
        await Shop.Ok(r, "upload");
        var url = (await Shop.Body(r)).GetProperty("imageUrl").GetString()!;
        Assert.EndsWith(".jpg", url);
        var served = await (await f.NewClient().GetAsync(url)).Content.ReadAsByteArrayAsync();
        using var got = SKBitmap.Decode(served);
        Assert.Equal(1200, got.Width); Assert.Equal(800, got.Height);
    }
}
