namespace Ecommerce.Api.Infrastructure.PaymentGateway
{
    public interface IPaymentGatewayService
    {
        Task<PaymentInitiateResult> InitiateAsync(string provider, int orderId, decimal amount);
        Task<PaymentVerifyResult> HandleCallbackAsync(string provider, string rawPayload);
    }

    // Routes a payment request to the matching IPaymentProviderClient by
    // Provider name (BKASH/NAGAD/BANGLAQR), so PaymentsController doesn't need
    // to know which concrete client to call.
    public class PaymentGatewayDispatcher : IPaymentGatewayService
    {
        private readonly IEnumerable<IPaymentProviderClient> _providers;
        private readonly IFileTextLogger _logger;

        public PaymentGatewayDispatcher(IEnumerable<IPaymentProviderClient> providers, IFileTextLogger logger)
        {
            _providers = providers;
            _logger = logger;
        }

        public async Task<PaymentInitiateResult> InitiateAsync(string provider, int orderId, decimal amount)
        {
            var client = _providers.FirstOrDefault(p => p.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase));
            if (client == null)
            {
                _logger.LogError("PAYMENT_GATEWAY", $"Unknown provider '{provider}'");
                return new PaymentInitiateResult { Success = false, Error = $"Unsupported payment provider '{provider}'." };
            }

            return await client.InitiateAsync(orderId, amount);
        }

        public async Task<PaymentVerifyResult> HandleCallbackAsync(string provider, string rawPayload)
        {
            var client = _providers.FirstOrDefault(p => p.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase));
            if (client == null)
            {
                _logger.LogError("PAYMENT_GATEWAY", $"Unknown provider '{provider}' on callback");
                return new PaymentVerifyResult { Success = false, Error = $"Unsupported payment provider '{provider}'." };
            }

            return await client.HandleCallbackAsync(rawPayload);
        }
    }
}
