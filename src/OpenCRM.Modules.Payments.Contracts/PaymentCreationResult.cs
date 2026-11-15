namespace OpenCRM.Modules.Payments.Contracts;

/// <summary> Результат создания платежа со статусом и реквизитами оплаты. </summary>
public sealed record PaymentCreationResult(
    PaymentCreationStatus Status,
    string? Message = null,
    string? PaymentId = null,
    string? PaymentUrl = null,
    Guid? PurchaseId = null);
