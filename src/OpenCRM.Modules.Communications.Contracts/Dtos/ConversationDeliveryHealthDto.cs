namespace OpenCRM.Modules.Communications.Contracts.Dtos;

/// <summary> Сводка очереди доставки для диагностики без захвата заданий. </summary>
public sealed record ConversationDeliveryHealthDto
{
    public long QueuedUnpublishedCount { get; init; }
    public long QueuedPublishedCount { get; init; }
    public DateTime? OldestQueuedAt { get; init; }
    public long StaleQueuedCount { get; init; }
    public DateTime? OldestStaleQueuedAt { get; init; }
    public long ExpiredSendingCount { get; init; }
    public DateTime? OldestExpiredSendingAt { get; init; }
    public long FailedCount { get; init; }
    public long UncertainCount { get; init; }
}
