namespace OpenCRM.Services.Interfaces.Features.Dtos;

/// <summary> Описание функциональной возможности. </summary>
public class FeatureDefinitionDto
{
    /// <summary> Идентификатор функциональной возможности. </summary>
    public int Id { get; set; }

    /// <summary> Код функциональной возможности. </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary> Наименование функциональной возможности. </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary> Описание функциональной возможности. </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary> Категория функциональной возможности. </summary>
    public string? Category { get; set; }

    /// <summary> Код области действия возможности. </summary>
    public string ScopeCode { get; set; } = string.Empty;

    /// <summary> Наименование области действия возможности. </summary>
    public string ScopeName { get; set; } = string.Empty;

    /// <summary> Код аудитории возможности. </summary>
    public string AudienceCode { get; set; } = string.Empty;

    /// <summary> Наименование аудитории возможности. </summary>
    public string AudienceName { get; set; } = string.Empty;

    /// <summary> Тип значения возможности. </summary>
    public string ValueType { get; set; } = string.Empty;

    /// <summary> Признак активности функциональной возможности. </summary>
    public bool IsActive { get; set; }

    /// <summary> Признак системной функциональной возможности. </summary>
    public bool IsSystem { get; set; }
}

