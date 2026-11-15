namespace Payments.Contracts;

/// <summary> Заказывает разовую оплату неизменяемой покупки подписки. </summary>
public sealed record CreatePayment(
    Guid CommandId,
    string ApplicationId,
    Guid PurchaseId,
    long AmountMinor,
    string Currency,
    string Description,
    string ReturnUrl,
    string BuyerId,
    string BeneficiaryId,
    string Purpose = "subscription",
    string InitiationMode = "customer",
    int SchemaVersion = 2,
    DateTimeOffset IssuedAt = default,
    DateTimeOffset NotAfter = default,
    string MerchantAccountId = "");
