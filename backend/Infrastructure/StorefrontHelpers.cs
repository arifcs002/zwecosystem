using System.Text.RegularExpressions;
using Ecommerce.Api.Domain;
using Microsoft.Extensions.Caching.Memory;

namespace Ecommerce.Api.Infrastructure
{
    public record CouponEvaluation(bool Ok, decimal Discount, string Message);

    /// <summary>The one place coupon eligibility + discount math lives (preview and checkout share it).</summary>
    public static class CouponRules
    {
        public static CouponEvaluation Evaluate(Coupon? coupon, decimal subtotal, DateTime nowUtc)
        {
            // Same message for "no such code" / "inactive" so codes can't be probed.
            const string invalid = "This coupon code is not valid.";
            if (coupon == null || !coupon.IsActive || coupon.IsDeleted != 0) return new(false, 0, invalid);
            if (coupon.StartsAt.HasValue && nowUtc < coupon.StartsAt.Value) return new(false, 0, "This coupon is not active yet.");
            if (coupon.ExpiresAt.HasValue && nowUtc > coupon.ExpiresAt.Value) return new(false, 0, "This coupon has expired.");
            if (coupon.UsageLimit.HasValue && coupon.UsedCount >= coupon.UsageLimit.Value)
                return new(false, 0, "This coupon has reached its usage limit.");
            if (subtotal < coupon.MinOrderAmount)
                return new(false, 0, $"Add ৳{coupon.MinOrderAmount:0.##} or more to use this coupon.");

            decimal discount = coupon.DiscountType == "FIXED"
                ? coupon.DiscountValue
                : subtotal * coupon.DiscountValue / 100m;
            if (coupon.DiscountType != "FIXED" && coupon.MaxDiscountAmount.HasValue)
                discount = Math.Min(discount, coupon.MaxDiscountAmount.Value);
            discount = Math.Round(Math.Min(discount, subtotal), 2, MidpointRounding.AwayFromZero);
            if (discount <= 0) return new(false, 0, invalid);
            return new(true, discount, "Coupon applied.");
        }
    }

    /// <summary>
    /// Size variants are separate product rows named "Shoe (Size 42)". Reviews belong to the
    /// shoe, not a size, so they are keyed by "<base name>::<categoryId>" — the SAME string the
    /// storefront computes in groupKey() (product-group.util.ts / shop-card.component.ts).
    /// </summary>
    public static class ProductKey
    {
        private static readonly Regex SizeSuffix = new(@"\s*\(Size [^)]+\)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string BaseName(string name) => SizeSuffix.Replace(name ?? "", "").Trim();
        public static string For(Product p) => $"{BaseName(p.Name)}::{p.CategoryId?.ToString() ?? ""}";
    }

    public static class PhoneUtil
    {
        private static readonly Regex Bd = new(@"^(?:\+?88)?(01[3-9]\d{8})$", RegexOptions.Compiled);

        /// <summary>Returns a canonical 01XXXXXXXXX number or null if it isn't a Bangladeshi mobile.</summary>
        public static string? NormalizeBd(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var cleaned = Regex.Replace(raw, @"[\s\-()]", "");
            var m = Bd.Match(cleaned);
            return m.Success ? m.Groups[1].Value : null;
        }

        public static string Last10Digits(string? raw)
        {
            var digits = new string((raw ?? "").Where(char.IsDigit).ToArray());
            return digits.Length <= 10 ? digits : digits[^10..];
        }
    }

    public static class RateLimit
    {
        /// <summary>True when the caller has exceeded <paramref name="limit"/> hits within the window.</summary>
        public static bool Exceeded(IMemoryCache cache, string key, int limit, TimeSpan window)
        {
            var entry = cache.GetOrCreate(key, e =>
            {
                e.AbsoluteExpirationRelativeToNow = window;
                return new Counter();
            })!;
            return Interlocked.Increment(ref entry.Hits) > limit;
        }

        public static string ClientKey(Microsoft.AspNetCore.Http.HttpContext http)
        {
            // nginx sets X-Real-IP from the TCP socket (a client can't forge it through nginx);
            // fall back to the socket address when the API is reached directly (dev/tests).
            var real = http.Request.Headers["X-Real-IP"].ToString().Trim();
            return !string.IsNullOrEmpty(real) ? real : http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        }

        private sealed class Counter { public int Hits; }
    }
}
