namespace OpenCRM.Modules.Payments.Contracts;

/// <summary> Текущее состояние покупки подписки и адрес основного действия после оплаты. </summary>
public sealed record PaymentPurchaseState(
    PaymentCreationResult Result,
    bool IsPaid,
    string PrimaryActionUrl);
