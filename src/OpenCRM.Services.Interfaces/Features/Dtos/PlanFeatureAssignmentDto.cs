namespace OpenCRM.Services.Interfaces.Features.Dtos;

/// <summary> Привязка функциональной возможности к тарифному плану. </summary>
public class PlanFeatureAssignmentDto
{
    /// <summary> Идентификатор функциональной возможности. </summary>
    public int FeatureId { get; set; }

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

    /// <summary> Признак привязки возможности к плану. </summary>
    public bool IsAssigned { get; set; }

    /// <summary> Логическое значение возможности в плане. </summary>
    public bool? BoolValue { get; set; }

    /// <summary> Целочисленное значение возможности в плане. </summary>
    public int? IntValue { get; set; }
}


