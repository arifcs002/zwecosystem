namespace Ecommerce.Api.Infrastructure.PaymentGateway
{
    public class PaymentInitiateResult
    {
        public bool Success { get; set; }
        public string? RedirectUrl { get; set; }
        public string? ProviderReference { get; set; }
        public string? Error { get; set; }
    }

    public class PaymentVerifyResult
    {
        public bool Success { get; set; }
        public string? TransactionId { get; set; }
        public int OrderId { get; set; }
        public decimal Amount { get; set; }
        public string? SenderNumber { get; set; }
        public string Status { get; set; } = "FAILED"; // 'SUCCESS', 'FAILED'
        public string? Error { get; set; }
    }

    // One implementation per gateway (bKash, Nagad, BanglaQR). Mirrors
    // ISmsSender/HttpSmsSender: best-effort, never throws — callers get a
    // result object with Success=false + Error instead of an exception.
    public interface IPaymentProviderClient
    {
        // Matches Payment.Provider values: 'BKASH', 'NAGAD', 'BANGLAQR'.
        string Provider { get; }

        Task<PaymentInitiateResult> InitiateAsync(int orderId, decimal amount);

        Task<PaymentVerifyResult> HandleCallbackAsync(string rawPayload);
    }
}
