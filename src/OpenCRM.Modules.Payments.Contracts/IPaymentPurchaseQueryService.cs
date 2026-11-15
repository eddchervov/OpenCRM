namespace OpenCRM.Modules.Payments.Contracts;

/// <summary> Читает состояние покупки подписки для её владельца. </summary>
public interface IPaymentPurchaseQueryService
{
    /// <summary> Возвращает состояние покупки или null, если она отсутствует либо принадлежит другому пользователю. </summary>
    Task<PaymentPurchaseState?> GetAsync(
        string userId,
        Guid purchaseId,
        CancellationToken cancellationToken = default);
}
