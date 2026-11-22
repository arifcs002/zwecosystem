using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Ecommerce.Api.Infrastructure
{
    public record CapiPurchase(
        string EventId, long EventTime, string? SourceUrl, string? Phone, string? Email,
        string? ClientIp, string? UserAgent, string? Fbp, string? Fbc, string? ExternalId,
        decimal Value, string Currency, IReadOnlyList<(int ProductId, int Quantity, decimal Price)> Items);

    public interface IMetaCapiClient
    {
        /// <summary>Sends a server-side Purchase event. Returns an error string, or null on success. Never throws.</summary>
        Task<string?> SendPurchaseAsync(string pixelId, string accessToken, string? testEventCode, CapiPurchase purchase, CancellationToken ct = default);
    }

    /// <summary>
    /// Meta Conversions API (server-side Purchase). Sent with the SAME event_id the browser pixel uses
    /// (the order number), so Meta de-duplicates the two. Personal data is SHA-256 hashed as Meta requires.
    /// NOTE: built from Meta's published Conversions API format and verified against a fake in tests only —
    /// confirm one real event in Meta Events Manager (use the "test event code") before relying on it.
    /// </summary>
    public class MetaCapiClient : IMetaCapiClient
    {
        public const string GraphVersion = "v21.0";
        private static readonly Regex PixelId = new(@"^\d{5,20}$", RegexOptions.Compiled);
        private readonly IHttpClientFactory _factory;
        public MetaCapiClient(IHttpClientFactory factory) { _factory = factory; }

        public static bool IsValidPixelId(string? id) => id != null && PixelId.IsMatch(id);

        public async Task<string?> SendPurchaseAsync(string pixelId, string accessToken, string? testEventCode, CapiPurchase p, CancellationToken ct = default)
        {
            if (!IsValidPixelId(pixelId)) return "invalid pixel id";
            try
            {
                var userData = new Dictionary<string, object?>();
                var ph = HashPhone(p.Phone);
                if (ph != null) userData["ph"] = new[] { ph };
                var em = HashEmail(p.Email);
                if (em != null) userData["em"] = new[] { em };
                if (!string.IsNullOrWhiteSpace(p.ExternalId)) userData["external_id"] = new[] { Sha256(p.ExternalId!) };
                if (!string.IsNullOrWhiteSpace(p.ClientIp)) userData["client_ip_address"] = p.ClientIp;
                if (!string.IsNullOrWhiteSpace(p.UserAgent)) userData["client_user_agent"] = p.UserAgent;
                if (!string.IsNullOrWhiteSpace(p.Fbp)) userData["fbp"] = p.Fbp;
                if (!string.IsNullOrWhiteSpace(p.Fbc)) userData["fbc"] = p.Fbc;

                var evt = new Dictionary<string, object?>
                {
                    ["event_name"] = "Purchase",
                    ["event_time"] = p.EventTime,
                    ["event_id"] = p.EventId,
                    ["action_source"] = "website",
                    ["event_source_url"] = p.SourceUrl,
                    ["user_data"] = userData,
                    ["custom_data"] = new
                    {
                        currency = p.Currency,
                        value = p.Value,
                        order_id = p.EventId,
                        content_type = "product",
                        content_ids = p.Items.Select(i => i.ProductId.ToString()).ToArray(),
                        contents = p.Items.Select(i => new { id = i.ProductId.ToString(), quantity = i.Quantity, item_price = i.Price }).ToArray()
                    }
                };
                var body = new Dictionary<string, object?> { ["data"] = new[] { evt } };
                if (!string.IsNullOrWhiteSpace(testEventCode)) body["test_event_code"] = testEventCode;

                var http = _factory.CreateClient();
                http.Timeout = TimeSpan.FromSeconds(10);
                var url = $"https://graph.facebook.com/{GraphVersion}/{pixelId}/events?access_token={Uri.EscapeDataString(accessToken)}";
                var resp = await http.PostAsJsonAsync(url, body, ct);
                if (resp.IsSuccessStatusCode) return null;
                var txt = await resp.Content.ReadAsStringAsync(ct);
                // Never echo the access token back into logs.
                return $"Meta responded {(int)resp.StatusCode}: " + (txt.Length > 200 ? txt[..200] : txt).Replace(accessToken, "***");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return "Could not reach Meta: " + ex.GetType().Name;
            }
        }

        // Meta wants lowercase, trimmed, then SHA-256 hex. Bangladeshi numbers go in E.164 digits (880…) without "+".
        public static string? HashPhone(string? raw)
        {
            var norm = PhoneUtil.NormalizeBd(raw);
            return norm == null ? null : Sha256("88" + norm);
        }

        public static string? HashEmail(string? email)
        {
            if (string.IsNullOrWhiteSpace(email)) return null;
            var e = email.Trim().ToLowerInvariant();
            return e.Contains('@') ? Sha256(e) : null;
        }

        public static string Sha256(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
    }
}
