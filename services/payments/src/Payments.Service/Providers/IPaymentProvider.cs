using Payments.Service.Domain;

namespace Payments.Service.Providers;

/// <summary> Выполняет только явные операции с сохранёнными телом и ключом. </summary>
public interface IPaymentProvider
{
    string BuildPaymentRequest(PaymentOrder order);
    string BuildRefundRequest(PaymentOrder order, long amountMinor, string reason);
    Task<ProviderPayment> CreatePaymentAsync(string requestBody, string idempotenceKey, CancellationToken cancellationToken);
    Task<ProviderPayment> GetPaymentAsync(string paymentId, CancellationToken cancellationToken);
    Task<ProviderRefund> CreateRefundAsync(string requestBody, string idempotenceKey, CancellationToken cancellationToken);
    Task<ProviderRefund> GetRefundAsync(string refundId, CancellationToken cancellationToken);
}
