using OpenCRM.Domain;

namespace OpenCRM.Modules.Promotions.Contracts;

/// <summary>
/// Методы работают в транзакции вызывающей стороны и не открывают свою.
/// </summary>
public interface IPromotionPaymentCoordinator
{
    /// <summary> Занимает применение промокода при создании платежа. </summary>
    Task ClaimAsync(PaymentCreationOperation operation);

    /// <summary> Привязывает промокод к созданному платежу по подписке. </summary>
    Task AttachPaymentAsync(PaymentCreationOperation operation, SubscriptionPayment payment);

    /// <summary> Потребляет применение промокода для организации. </summary>
    Task ConsumeAsync(SubscriptionPayment payment, Guid organizationId);
}
