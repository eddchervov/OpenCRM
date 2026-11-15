namespace OpenCRM.Services.Interfaces.Features.Dtos;

/// <summary> Значение функции тарифа. </summary>
public class FeatureValueDto
{
    /// <summary> Идентификатор функции. </summary>
    public int FeatureId { get; set; }

    /// <summary> Код функции. </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary> Название функции. </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary> Категория функции. </summary>
    public string? Category { get; set; }

    /// <summary> Код области действия функции. </summary>
    public string ScopeCode { get; set; } = string.Empty;

    /// <summary> Название области действия функции. </summary>
    public string ScopeName { get; set; } = string.Empty;

    /// <summary> Код аудитории функции. </summary>
    public string AudienceCode { get; set; } = string.Empty;

    /// <summary> Название аудитории функции. </summary>
    public string AudienceName { get; set; } = string.Empty;

    /// <summary> Тип значения функции. </summary>
    public string ValueType { get; set; } = string.Empty;

    /// <summary> Логическое значение функции. </summary>
    public bool? BoolValue { get; set; }

    /// <summary> Целочисленное значение функции. </summary>
    public int? IntValue { get; set; }
}

