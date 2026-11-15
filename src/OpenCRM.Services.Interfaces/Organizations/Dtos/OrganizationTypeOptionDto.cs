namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> DTO варианта типа организации. </summary>
public class OrganizationTypeOptionDto
{
    /// <summary> Идентификатор типа организации. </summary>
    public int Id { get; set; }

    /// <summary> Код типа организации. </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary> Название типа организации. </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary> Описание типа организации. </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary> Признак активности типа. </summary>
    public bool IsActive { get; set; }

    /// <summary> Порядок сортировки. </summary>
    public int SortOrder { get; set; }
}
