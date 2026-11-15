namespace Payments.Contracts;

/// <summary> Сообщает о результате отдельной операции, не меняя состояние оплаты. </summary>
public sealed record PaymentOperationStateChanged(
    Guid EventId,
    string ApplicationId,
    Guid PurchaseId,
    Guid PaymentOrderId,
    Guid OperationId,
    Guid CommandId,
    string Kind,
    string Status,
    string? ErrorCode,
    DateTimeOffset OccurredAt,
    int SchemaVersion = 1);
