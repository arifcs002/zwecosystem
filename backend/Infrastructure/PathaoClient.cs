using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace Ecommerce.Api.Infrastructure
{
    public record PathaoCreds(string ClientId, string ClientSecret, string Username, string Password, string StoreId, bool Sandbox);

    public interface IPathaoClient
    {
        Task<CourierBooking> CreateOrderAsync(PathaoCreds c, string invoice, string name, string phone, string address,
            decimal codAmount, string? note, CancellationToken ct = default);
        Task<CourierStatusResult> GetStatusAsync(PathaoCreds c, string consignmentId, CancellationToken ct = default);
    }

    /// <summary>
    /// Pathao Courier merchant API ("aladdin" v1). Credentials are per shop and passed in by the caller.
    /// NOTE: written from Pathao's published API description and verified against a fake in tests only —
    /// book one real test parcel (sandbox first) with the shop's own credentials before relying on it.
    /// </summary>
    public class PathaoClient : IPathaoClient
    {
        public const string HttpClientName = "pathao";
        private const string Live = "https://api-hermes.pathao.com";
        private const string Sandbox = "https://courier-api-sandbox.pathao.com";
        private readonly IHttpClientFactory _factory;
        private readonly IMemoryCache _cache;
        public PathaoClient(IHttpClientFactory factory, IMemoryCache cache) { _factory = factory; _cache = cache; }

        private static string Base(PathaoCreds c) => c.Sandbox ? Sandbox : Live;

        public async Task<CourierBooking> CreateOrderAsync(PathaoCreds c, string invoice, string name, string phone, string address,
            decimal codAmount, string? note, CancellationToken ct = default)
        {
            try
            {
                var (http, err) = await Authed(c, ct);
                if (http == null) return new(false, null, null, null, err);
                var resp = await http.PostAsJsonAsync($"{Base(c)}/aladdin/api/v1/orders", new
                {
                    store_id = int.TryParse(c.StoreId, out var sid) ? sid : 0,
                    merchant_order_id = invoice,
                    recipient_name = name,
                    recipient_phone = phone,
                    recipient_address = address,
                    delivery_type = 48,        // normal delivery
                    item_type = 2,             // parcel
                    item_quantity = 1,
                    item_weight = 0.5,
                    amount_to_collect = (int)Math.Round(codAmount),
                    special_instruction = note
                }, ct);
                var body = await resp.Content.ReadAsStringAsync(ct);
                if (!resp.IsSuccessStatusCode) return new(false, null, null, null, Short(body, (int)resp.StatusCode));

                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object)
                {
                    var id = Str(d, "consignment_id");
                    if (!string.IsNullOrEmpty(id)) return new(true, id, id, Str(d, "order_status") ?? "Pending", null);
                }
                return new(false, null, null, null, Str(doc.RootElement, "message") ?? Short(body, (int)resp.StatusCode));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                return new(false, null, null, null, "Could not reach Pathao: " + ex.Message);
            }
        }

        public async Task<CourierStatusResult> GetStatusAsync(PathaoCreds c, string consignmentId, CancellationToken ct = default)
        {
            try
            {
                var (http, err) = await Authed(c, ct);
                if (http == null) return new(false, null, err);
                var resp = await http.GetAsync($"{Base(c)}/aladdin/api/v1/orders/{Uri.EscapeDataString(consignmentId)}/info", ct);
                var body = await resp.Content.ReadAsStringAsync(ct);
                if (!resp.IsSuccessStatusCode) return new(false, null, Short(body, (int)resp.StatusCode));
                using var doc = JsonDocument.Parse(body);
                var status = doc.RootElement.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object ? Str(d, "order_status") : null;
                return status == null ? new(false, null, "Pathao returned no status.") : new(true, status, null);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                return new(false, null, "Could not reach Pathao: " + ex.Message);
            }
        }

        // Pathao issues a bearer token from the merchant's login; cache it per account until shortly before it expires.
        private async Task<(HttpClient? Http, string? Error)> Authed(PathaoCreds c, CancellationToken ct)
        {
            var cacheKey = "pathao-token:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes($"{c.Sandbox}|{c.ClientId}|{c.ClientSecret}|{c.Username}|{c.Password}")));
            if (!_cache.TryGetValue(cacheKey, out string? token) || string.IsNullOrEmpty(token))
            {
                var http0 = _factory.CreateClient(HttpClientName);
                var resp = await http0.PostAsJsonAsync($"{Base(c)}/aladdin/api/v1/issue-token", new
                {
                    client_id = c.ClientId, client_secret = c.ClientSecret,
                    username = c.Username, password = c.Password, grant_type = "password"
                }, ct);
                var body = await resp.Content.ReadAsStringAsync(ct);
                if (!resp.IsSuccessStatusCode) return (null, "Pathao login failed — check your Pathao credentials. " + Short(body, (int)resp.StatusCode));
                using var doc = JsonDocument.Parse(body);
                token = Str(doc.RootElement, "access_token");
                if (string.IsNullOrEmpty(token)) return (null, "Pathao login returned no token.");
                var secs = doc.RootElement.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var n) ? n : 86400;
                _cache.Set(cacheKey, token, TimeSpan.FromSeconds(Math.Max(300, secs - 3600)));
            }
            var http = _factory.CreateClient(HttpClientName);
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return (http, null);
        }

        private static string? Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null
                ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString()) : null;

        private static string Short(string body, int status)
        {
            body = (body ?? "").Trim();
            return $"Pathao responded {status}" + (body.Length == 0 ? "." : ": " + (body.Length > 200 ? body[..200] : body));
        }
    }
}
