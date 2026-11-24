using SkiaSharp;

namespace Ecommerce.Api.Infrastructure
{
    // Re-encodes every uploaded picture as a JPEG no bigger than 1200px. Uses SkiaSharp (MIT licence, no key needed).
    // Anything that is not a picture Skia can decode (an .html or .svg with a script inside, a renamed .exe) is refused,
    // so it can never be stored and served from our own address.
    public static class ImageProcessor
    {
        public const int MaxDimension = 1200;
        public const int MaxPixels = 40_000_000;      // refuse decompression bombs (a tiny file that expands to gigabytes)
        public const int JpegQuality = 82;

        // Returns the JPEG bytes, or null when the data is not a usable picture.
        public static byte[]? ToJpeg(Stream input)
        {
            using var ms = new MemoryStream();
            input.CopyTo(ms);
            ms.Position = 0;
            using var codec = SKCodec.Create(ms);
            if (codec == null) return null;
            var info = codec.Info;
            if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > MaxPixels) return null;

            using var decoded = SKBitmap.Decode(codec, new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
            if (decoded == null) return null;
            using var oriented = Orient(decoded, codec.EncodedOrigin);

            var scale = Math.Min(1.0, (double)MaxDimension / Math.Max(oriented.Width, oriented.Height));
            var w = Math.Max(1, (int)Math.Round(oriented.Width * scale));
            var h = Math.Max(1, (int)Math.Round(oriented.Height * scale));

            using var target = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Opaque));
            using (var canvas = new SKCanvas(target))
            {
                canvas.Clear(SKColors.White);                 // transparent PNGs get a white background, not black
                using var image = SKImage.FromBitmap(oriented);
                canvas.DrawImage(image, new SKRect(0, 0, w, h), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            }
            using var final = SKImage.FromBitmap(target);
            using var data = final.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
            return data?.ToArray();
        }

        // Phones store photos sideways and add an EXIF "orientation" note. Re-encoding drops that note, so the pixels are turned
        // to the right way up here instead; otherwise phone photos would show rotated.
        public static SKBitmap Orient(SKBitmap src, SKEncodedOrigin origin)
        {
            int w = src.Width, h = src.Height;
            var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
            var dst = new SKBitmap(new SKImageInfo(swap ? h : w, swap ? w : h, src.ColorType, src.AlphaType));
            using var canvas = new SKCanvas(dst);
            canvas.Clear(SKColors.Transparent);
            switch (origin)
            {
                case SKEncodedOrigin.TopRight: canvas.Translate(w, 0); canvas.Scale(-1, 1); break;
                case SKEncodedOrigin.BottomRight: canvas.Translate(w, h); canvas.RotateDegrees(180); break;
                case SKEncodedOrigin.BottomLeft: canvas.Translate(0, h); canvas.Scale(1, -1); break;
                case SKEncodedOrigin.LeftTop: canvas.Concat(new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1)); break;
                case SKEncodedOrigin.RightTop: canvas.Translate(h, 0); canvas.RotateDegrees(90); break;
                case SKEncodedOrigin.RightBottom: canvas.Concat(new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1)); break;
                case SKEncodedOrigin.LeftBottom: canvas.Translate(0, w); canvas.RotateDegrees(270); break;
            }
            canvas.DrawBitmap(src, 0, 0);
            return dst;
        }
    }
}
