using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Api.Infrastructure.PaymentGateway
{
    // BanglaQR checkout integration — same shape as BkashProvider/NagadProvider.
    // Credentials read per-company from CompanySettings (banglaqr_base_url,
    // banglaqr_api_key).
    public class BanglaQrProvider : IPaymentProviderClient
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IFileTextLogger _logger;

        public string Provider => "BANGLAQR";

        public BanglaQrProvider(ApplicationDbContext context, IHttpClientFactory httpClientFactory, IFileTextLogger logger)
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

                var baseUrl = settings.GetValueOrDefault("banglaqr_base_url");
                if (string.IsNullOrWhiteSpace(baseUrl))
                    return new PaymentInitiateResult { Success = false, Error = "BanglaQR is not configured for this store." };

                var apiKey = settings.GetValueOrDefault("banglaqr_api_key");
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(15);
                if (!string.IsNullOrWhiteSpace(apiKey))
                    client.DefaultRequestHeaders.Add("Authorization", apiKey);

                var url = $"{baseUrl}?orderId={orderId}&amount={amount}";
                var resp = await client.PostAsync(url, content: null);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogError("PAYMENT_BANGLAQR", $"Initiate returned {(int)resp.StatusCode} for order {orderId}");
                    return new PaymentInitiateResult { Success = false, Error = "BanglaQR gateway rejected the request." };
                }

                var body = await resp.Content.ReadAsStringAsync();
                _logger.LogInfo("PAYMENT_BANGLAQR", $"Initiated for order {orderId}");
                return new PaymentInitiateResult { Success = true, RedirectUrl = body, ProviderReference = orderId.ToString() };
            }
            catch (Exception ex)
            {
                _logger.LogError("PAYMENT_BANGLAQR", $"Initiate failed for order {orderId}", ex);
                return new PaymentInitiateResult { Success = false, Error = "BanglaQR gateway error." };
            }
        }

        public Task<PaymentVerifyResult> HandleCallbackAsync(string rawPayload)
        {
            try
            {
                _logger.LogInfo("PAYMENT_BANGLAQR", "Callback received");
                return Task.FromResult(new PaymentVerifyResult { Success = false, Error = "Callback parsing not yet implemented." });
            }
            catch (Exception ex)
            {
                _logger.LogError("PAYMENT_BANGLAQR", "Callback handling failed", ex);
                return Task.FromResult(new PaymentVerifyResult { Success = false, Error = "BanglaQR callback error." });
            }
        }
    }
}
