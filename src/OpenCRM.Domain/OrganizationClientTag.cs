namespace OpenCRM.Domain;

/// <summary> Тег для маркировки клиентов организации. </summary>
public class OrganizationClientTag
{
    /// <summary> Идентификатор тега. </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary> Идентификатор организации. </summary>
    public Guid OrganizationId { get; set; }

    /// <summary> Организация, к которой относится тег. </summary>
    public Organization Organization { get; set; } = null!;

    /// <summary> Название тега. </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary> Нормализованное название тега. </summary>
    public string NormalizedName { get; set; } = string.Empty;

    /// <summary> Дата и время создания тега. </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary> Закрепления тега за клиентами. </summary>
    public ICollection<OrganizationClientTagAssignment> ClientAssignments { get; set; } = new HashSet<OrganizationClientTagAssignment>();
}
