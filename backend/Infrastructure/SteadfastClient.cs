using System.Net.Http.Json;
using System.Text.Json;

namespace Ecommerce.Api.Infrastructure
{
    public record CourierBooking(bool Ok, string? ConsignmentId, string? TrackingCode, string? Status, string? Error);
    public record CourierStatusResult(bool Ok, string? Status, string? Error);

    public interface ISteadfastClient
    {
        Task<CourierBooking> CreateOrderAsync(string apiKey, string secretKey, string invoice, string name,
            string phone, string address, decimal codAmount, string? note, CancellationToken ct = default);
        Task<CourierStatusResult> GetStatusAsync(string apiKey, string secretKey, string consignmentId, CancellationToken ct = default);
    }

    /// <summary>
    /// Steadfast Courier merchant API (https://portal.packzy.com/api/v1). Credentials are per shop and
    /// passed in by the caller (never read from global config). Responses are parsed defensively.
    /// NOTE: written from Steadfast's published API description and verified against a fake server in
    /// tests only — run one real test parcel with the shop's own credentials before relying on it.
    /// </summary>
    public class SteadfastClient : ISteadfastClient
    {
        public const string HttpClientName = "steadfast";
        private readonly IHttpClientFactory _factory;
        public SteadfastClient(IHttpClientFactory factory) { _factory = factory; }

        public async Task<CourierBooking> CreateOrderAsync(string apiKey, string secretKey, string invoice, string name,
            string phone, string address, decimal codAmount, string? note, CancellationToken ct = default)
        {
            try
            {
                var http = Client(apiKey, secretKey);
                var resp = await http.PostAsJsonAsync("create_order", new
                {
                    invoice,
                    recipient_name = name,
                    recipient_phone = phone,
                    recipient_address = address,
                    cod_amount = codAmount,
                    note
                }, ct);
                var body = await resp.Content.ReadAsStringAsync(ct);
                if (!resp.IsSuccessStatusCode) return new(false, null, null, null, Short(body, (int)resp.StatusCode));

                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.TryGetProperty("consignment", out var c) && c.ValueKind == JsonValueKind.Object)
                {
                    var id = Str(c, "consignment_id");
                    var tracking = Str(c, "tracking_code");
                    if (!string.IsNullOrEmpty(id) || !string.IsNullOrEmpty(tracking))
                        return new(true, id, tracking, Str(c, "status") ?? "in_review", null);
                }
                return new(false, null, null, null, Str(root, "message") ?? Short(body, (int)resp.StatusCode));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                return new(false, null, null, null, "Could not reach Steadfast: " + ex.Message);
            }
        }

        public async Task<CourierStatusResult> GetStatusAsync(string apiKey, string secretKey, string consignmentId, CancellationToken ct = default)
        {
            try
            {
                var http = Client(apiKey, secretKey);
                var resp = await http.GetAsync($"status_by_cid/{Uri.EscapeDataString(consignmentId)}", ct);
                var body = await resp.Content.ReadAsStringAsync(ct);
                if (!resp.IsSuccessStatusCode) return new(false, null, Short(body, (int)resp.StatusCode));
                using var doc = JsonDocument.Parse(body);
                var status = Str(doc.RootElement, "delivery_status");
                return status == null ? new(false, null, "Steadfast returned no status.") : new(true, status, null);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                return new(false, null, "Could not reach Steadfast: " + ex.Message);
            }
        }

        private HttpClient Client(string apiKey, string secretKey)
        {
            var http = _factory.CreateClient(HttpClientName);
            http.DefaultRequestHeaders.Remove("Api-Key");
            http.DefaultRequestHeaders.Remove("Secret-Key");
            http.DefaultRequestHeaders.Add("Api-Key", apiKey);
            http.DefaultRequestHeaders.Add("Secret-Key", secretKey);
            return http;
        }

        private static string? Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null
                ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString()) : null;

        private static string Short(string body, int status)
        {
            body = (body ?? "").Trim();
            return $"Steadfast responded {status}" + (body.Length == 0 ? "." : ": " + (body.Length > 200 ? body[..200] : body));
        }
    }
}
