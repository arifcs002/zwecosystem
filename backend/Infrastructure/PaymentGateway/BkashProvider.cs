using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Api.Infrastructure.PaymentGateway
{
    // bKash checkout integration. Like HttpSmsSender, credentials/base URL are
    // read per-company from CompanySettings (keys: bkash_base_url, bkash_api_key)
    // rather than hard-coded, since each tenant has their own merchant account.
    // Best-effort: never throws, returns a result object with Error set instead.
    public class BkashProvider : IPaymentProviderClient
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IFileTextLogger _logger;

        public string Provider => "BKASH";

        public BkashProvider(ApplicationDbContext context, IHttpClientFactory httpClientFactory, IFileTextLogger logger)
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

                var baseUrl = settings.GetValueOrDefault("bkash_base_url");
                if (string.IsNullOrWhiteSpace(baseUrl))
                    return new PaymentInitiateResult { Success = false, Error = "bKash is not configured for this store." };

                var apiKey = settings.GetValueOrDefault("bkash_api_key");
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(15);
                if (!string.IsNullOrWhiteSpace(apiKey))
                    client.DefaultRequestHeaders.Add("Authorization", apiKey);

                var url = $"{baseUrl}?orderId={orderId}&amount={amount}";
                var resp = await client.PostAsync(url, content: null);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogError("PAYMENT_BKASH", $"Initiate returned {(int)resp.StatusCode} for order {orderId}");
                    return new PaymentInitiateResult { Success = false, Error = "bKash gateway rejected the request." };
                }

                var body = await resp.Content.ReadAsStringAsync();
                _logger.LogInfo("PAYMENT_BKASH", $"Initiated for order {orderId}");
                return new PaymentInitiateResult { Success = true, RedirectUrl = body, ProviderReference = orderId.ToString() };
            }
            catch (Exception ex)
            {
                _logger.LogError("PAYMENT_BKASH", $"Initiate failed for order {orderId}", ex);
                return new PaymentInitiateResult { Success = false, Error = "bKash gateway error." };
            }
        }

        public Task<PaymentVerifyResult> HandleCallbackAsync(string rawPayload)
        {
            // bKash callback payload parsing (merchantInvoiceNumber, trxID, amount,
            // paymentID, msisdn) — provider-specific mapping goes here once the
            // exact callback contract for this tenant's bKash account is confirmed.
            try
            {
                _logger.LogInfo("PAYMENT_BKASH", "Callback received");
                return Task.FromResult(new PaymentVerifyResult { Success = false, Error = "Callback parsing not yet implemented." });
            }
            catch (Exception ex)
            {
                _logger.LogError("PAYMENT_BKASH", "Callback handling failed", ex);
                return Task.FromResult(new PaymentVerifyResult { Success = false, Error = "bKash callback error." });
            }
        }
    }
}
