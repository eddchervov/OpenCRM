namespace OpenCRM.Domain;

/// <summary> Название и доступность типа организации. </summary>
public class OrganizationTypeDefinition
{
    public int Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary> Тип доступен для новых организаций; остальные записи сохраняют исторические названия. </summary>
    public bool IsSupported { get; set; }

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
