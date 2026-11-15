namespace OpenCRM.Domain;

/// <summary> Группа шаблонов сообщений организации. </summary>
public sealed class MessageTemplateGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public int Version { get; set; } = 1;
    public bool IsArchived { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<MessageTemplate> Templates { get; set; } = new HashSet<MessageTemplate>();
}
