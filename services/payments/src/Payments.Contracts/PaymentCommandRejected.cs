namespace Payments.Contracts;

/// <summary> Сообщает, что команда отклонена до создания новой денежной операции. </summary>
public sealed record PaymentCommandRejected(
    Guid EventId,
    string ApplicationId,
    Guid PurchaseId,
    Guid CommandId,
    string Reason,
    DateTimeOffset OccurredAt,
    int SchemaVersion = 1);
