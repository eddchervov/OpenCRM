namespace Payments.Contracts;

/// <summary> Запрашивает возврат части или всей суммы ранее принятого платежа. </summary>
public sealed record RequestRefund(
    Guid CommandId,
    string ApplicationId,
    Guid PurchaseId,
    long AmountMinor,
    string Reason,
    string RequestedBy,
    int SchemaVersion = 2,
    DateTimeOffset IssuedAt = default,
    DateTimeOffset NotAfter = default,
    string MerchantAccountId = "");
