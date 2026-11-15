namespace OpenCRM.Domain;

/// <summary> Диагностическое событие использования приложения без содержимого пользовательских форм. </summary>
public sealed class UserActivityEvent
{
    public long Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public Guid? OrganizationId { get; set; }

    public Guid SessionId { get; set; }

    public DateTime OccurredAt { get; set; }

    public DateTime ReceivedAt { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string Page { get; set; } = string.Empty;

    public string Scenario { get; set; } = string.Empty;

    public string Section { get; set; } = string.Empty;

    public string? TargetId { get; set; }
}
