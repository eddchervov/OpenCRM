namespace OpenCRM.Domain;

/// <summary> Transactional outbox для wakeup публикации ответа в транспортную очередь. </summary>
public sealed class OrganizationConversationDispatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MessageId { get; set; }
    public OrganizationConversationMessage Message { get; set; } = null!;
    public Guid RequestId { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTime? LeaseExpiresAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime? NextReconcileAt { get; set; }
    public int AttemptCount { get; set; }
    public int PublicationReplayCount { get; set; }
    public string? LastErrorCode { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
