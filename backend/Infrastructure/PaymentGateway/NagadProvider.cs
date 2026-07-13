using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Api.Infrastructure.PaymentGateway
{
    // Nagad checkout integration — same shape as BkashProvider. Credentials
    // read per-company from CompanySettings (nagad_base_url, nagad_api_key).
    public class NagadProvider : IPaymentProviderClient
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IFileTextLogger _logger;

        public string Provider => "NAGAD";

        public NagadProvider(ApplicationDbContext context, IHttpClientFactory httpClientFactory, IFileTextLogger logger)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<PaymentInitiateResult> InitiateAsync(int orderId, decimal amount)
        {
            try
            {
                var companyId = _context.CompanyId;
                if (!companyId.HasValue)
                    return new PaymentInitiateResult { Success = false, Error = "Company context is required." };

                var settings = await _context.CompanySettings
                    .Where(s => s.CompanyId == companyId.Value)
                    .ToDictionaryAsync(s => s.Key, s => s.Value);

                var baseUrl = settings.GetValueOrDefault("nagad_base_url");
                if (string.IsNullOrWhiteSpace(baseUrl))
                    return new PaymentInitiateResult { Success = false, Error = "Nagad is not configured for this store." };

                var apiKey = settings.GetValueOrDefault("nagad_api_key");
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(15);
                if (!string.IsNullOrWhiteSpace(apiKey))
                    client.DefaultRequestHeaders.Add("Authorization", apiKey);

                var url = $"{baseUrl}?orderId={orderId}&amount={amount}";
                var resp = await client.PostAsync(url, content: null);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogError("PAYMENT_NAGAD", $"Initiate returned {(int)resp.StatusCode} for order {orderId}");
                    return new PaymentInitiateResult { Success = false, Error = "Nagad gateway rejected the request." };
                }

                var body = await resp.Content.ReadAsStringAsync();
                _logger.LogInfo("PAYMENT_NAGAD", $"Initiated for order {orderId}");
                return new PaymentInitiateResult { Success = true, RedirectUrl = body, ProviderReference = orderId.ToString() };
            }
            catch (Exception ex)
            {
                _logger.LogError("PAYMENT_NAGAD", $"Initiate failed for order {orderId}", ex);
                return new PaymentInitiateResult { Success = false, Error = "Nagad gateway error." };
            }
        }

        public Task<PaymentVerifyResult> HandleCallbackAsync(string rawPayload)
        {
            try
            {
                _logger.LogInfo("PAYMENT_NAGAD", "Callback received");
                return Task.FromResult(new PaymentVerifyResult { Success = false, Error = "Callback parsing not yet implemented." });
            }
            catch (Exception ex)
            {
                _logger.LogError("PAYMENT_NAGAD", "Callback handling failed", ex);
                return Task.FromResult(new PaymentVerifyResult { Success = false, Error = "Nagad callback error." });
            }
        }
    }
}
