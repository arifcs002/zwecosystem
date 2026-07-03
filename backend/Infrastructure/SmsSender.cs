using System.Net;

namespace Ecommerce.Api.Infrastructure
{
    public interface ISmsSender
    {
        // Sends an SMS using the given company's configured gateway URL template.
        // No-op (returns false) if the company hasn't configured one. Never throws
        // — SMS is best-effort and must not break the order flow.
        Task<bool> SendAsync(string? apiUrlTemplate, string toNumber, string message);
    }

    // Provider-agnostic sender. Most Bangladeshi SMS gateways (bulksmsbd,
    // greenweb, MIMSMS, etc.) expose a plain HTTP endpoint where you pass the
    // number + message as query params. Rather than hard-code one provider, the
    // company pastes their full URL into Settings with {to} and {msg}
    // placeholders, e.g.:
    //   http://bulksmsbd.net/api/smsapi?api_key=KEY&type=text&senderid=SID&number={to}&message={msg}
    public class HttpSmsSender : ISmsSender
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IFileTextLogger _logger;

        public HttpSmsSender(IHttpClientFactory httpClientFactory, IFileTextLogger logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<bool> SendAsync(string? apiUrlTemplate, string toNumber, string message)
        {
            if (string.IsNullOrWhiteSpace(apiUrlTemplate) || string.IsNullOrWhiteSpace(toNumber))
                return false;

            try
            {
                var url = apiUrlTemplate
                    .Replace("{to}", WebUtility.UrlEncode(toNumber))
                    .Replace("{msg}", WebUtility.UrlEncode(message));

                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                var resp = await client.GetAsync(url);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogError("SMS", $"Gateway returned {(int)resp.StatusCode} for {toNumber}");
                    return false;
                }
                _logger.LogInfo("SMS", $"Sent to {toNumber}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("SMS", $"Failed to send to {toNumber}", ex);
                return false;
            }
        }
    }
}
