using Payments.Service.Domain;

namespace Payments.Service.Providers;

/// <summary> Блокирует сетевые денежные операции, пока провайдер явно не включён. </summary>
public sealed class DisabledPaymentProvider : IPaymentProvider
{
    public string BuildPaymentRequest(PaymentOrder order) => YooKassaPaymentProvider.BuildPaymentRequestBody(order);

    public string BuildRefundRequest(PaymentOrder order, long amountMinor, string reason) =>
        YooKassaPaymentProvider.BuildRefundRequestBody(order, amountMinor, reason);

    public Task<ProviderPayment> CreatePaymentAsync(string requestBody, string idempotenceKey, CancellationToken cancellationToken) =>
        throw PaymentDisabled();

    public Task<ProviderPayment> GetPaymentAsync(string paymentId, CancellationToken cancellationToken) =>
        throw PaymentDisabled();

    public Task<ProviderRefund> CreateRefundAsync(string requestBody, string idempotenceKey, CancellationToken cancellationToken) =>
        throw PaymentDisabled();

    public Task<ProviderRefund> GetRefundAsync(string refundId, CancellationToken cancellationToken) =>
        throw PaymentDisabled();

    private static PaymentProviderException PaymentDisabled() =>
        new("provider_disabled", requiresManualReview: true);
}
