using System.Text.Json;

namespace Ecommerce.Api.Infrastructure
{
    public record SpecDto(string Key, string Value);

    /// Unit label (pair, kg, dozen…) and the product's spec list (Color: Black …).
    /// Both are display data only — stock stays a whole number.
    public static class ProductSpecs
    {
        public const int MaxUom = 20, MaxSpecs = 20, MaxKey = 40, MaxValue = 120;

        public static string? CleanUom(string? uom)
        {
            var u = (uom ?? "").Trim();
            return u.Length == 0 ? null : (u.Length > MaxUom ? u[..MaxUom] : u);
        }

        /// Trims, drops blanks, de-duplicates by key (first wins), caps length/count. Returns null when empty.
        public static string? ToJson(IEnumerable<SpecDto>? specs)
        {
            if (specs == null) return null;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<SpecDto>();
            foreach (var s in specs)
            {
                var k = (s.Key ?? "").Trim(); var v = (s.Value ?? "").Trim();
                if (k.Length == 0 || v.Length == 0 || !seen.Add(k)) continue;
                list.Add(new SpecDto(k.Length > MaxKey ? k[..MaxKey] : k, v.Length > MaxValue ? v[..MaxValue] : v));
                if (list.Count == MaxSpecs) break;
            }
            return list.Count == 0 ? null : JsonSerializer.Serialize(list);
        }

        public static List<SpecDto> Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new();
            try { return JsonSerializer.Deserialize<List<SpecDto>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new(); }
            catch (JsonException) { return new(); }
        }
    }
}
