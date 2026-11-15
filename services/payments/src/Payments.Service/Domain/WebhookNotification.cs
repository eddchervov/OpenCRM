namespace Payments.Service.Domain;

/// <summary> Сохраняет непроверенный сигнал провайдера до подтверждения получения. </summary>
public sealed class WebhookNotification
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string ObjectId { get; set; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
}
