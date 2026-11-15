namespace OpenCRM.Domain;

/// <summary> Версионируемый текстовый шаблон организации. </summary>
public sealed class MessageTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid GroupId { get; set; }
    public MessageTemplateGroup Group { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public int SortOrder { get; set; }
    public bool IsArchived { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
