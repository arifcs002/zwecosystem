using System.Text.Json;
using Ecommerce.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Ecommerce.Api.Infrastructure
{
    /// What a company's plan allows right now. Plan features live in SubscriptionPlan.Features (JSON):
    /// module flags (pos_enabled, ecommerce_enabled, multi_staff) and limits (max_products).
    /// A flag that is ABSENT means "allowed" so custom plans keep working; only an explicit false blocks.
    public record PlanState(
        int CompanyId, int? PlanId, string? PlanName, DateTime? ExpiresAt,
        IReadOnlyDictionary<string, JsonElement> Features)
    {
        public const int GraceDays = 7;

        public bool IsEnabled(string flag) =>
            !(Features.TryGetValue(flag, out var v) && v.ValueKind == JsonValueKind.False);

        public int? Limit(string key) =>
            Features.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;

        /// active | grace (expired, still within GraceDays) | expired. No expiry date = active (never billed).
        public string Status(DateTime now) =>
            ExpiresAt == null || ExpiresAt > now ? "active"
            : ExpiresAt.Value.AddDays(GraceDays) > now ? "grace" : "expired";

        public int? DaysLeft(DateTime now) => ExpiresAt == null ? null : (int)Math.Ceiling((ExpiresAt.Value - now).TotalDays);
    }

    public static class PlanAccess
    {
        public static int TrialDays =>
            int.TryParse(Environment.GetEnvironmentVariable("TRIAL_DAYS"), out var d) && d >= 0 && d <= 365 ? d : 14;

        public static IReadOnlyDictionary<string, JsonElement> ParseFeatures(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, JsonElement>();
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return new Dictionary<string, JsonElement>();
                return doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
            }
            catch (JsonException) { return new Dictionary<string, JsonElement>(); }
        }

        /// Cached 30s per company; call Invalidate after any plan / payment change.
        public static async Task<PlanState?> GetAsync(ApplicationDbContext db, IMemoryCache cache, int companyId)
        {
            var key = $"plan-state:{companyId}";
            if (cache.TryGetValue(key, out PlanState? hit)) return hit;
            var row = await db.Companies.IgnoreQueryFilters().AsNoTracking()
                .Where(c => c.Id == companyId)
                .Select(c => new { c.Id, c.SubscriptionPlanId, PlanName = c.SubscriptionPlan != null ? c.SubscriptionPlan.Name : null,
                                   Features = c.SubscriptionPlan != null ? c.SubscriptionPlan.Features : null, c.SubscriptionExpiresAt })
                .FirstOrDefaultAsync();
            var state = row == null ? null
                : new PlanState(row.Id, row.SubscriptionPlanId, row.PlanName, row.SubscriptionExpiresAt, ParseFeatures(row.Features));
            cache.Set(key, state, TimeSpan.FromSeconds(30));
            return state;
        }

        public static void Invalidate(IMemoryCache cache, int companyId) => cache.Remove($"plan-state:{companyId}");

        /// Which module flag a request path needs, if any.
        public static string? ModuleFor(string path)
        {
            path = path.ToLowerInvariant();
            if (path.StartsWith("/api/pos")) return "pos_enabled";
            if (path.StartsWith("/api/storefront") || path.StartsWith("/api/orders/public")) return "ecommerce_enabled";
            return null;
        }

        public static string ModuleLabel(string flag) => flag switch
        {
            "pos_enabled" => "Point of Sale",
            "ecommerce_enabled" => "the online store",
            "multi_staff" => "multiple staff accounts",
            _ => flag
        };
    }
}
