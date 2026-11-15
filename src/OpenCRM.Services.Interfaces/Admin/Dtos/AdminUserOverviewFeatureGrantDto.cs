namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO предоставленной функции в обзоре пользователя. </summary>
public sealed class AdminUserOverviewFeatureGrantDto
{
    /// <summary> Идентификатор предоставления функции. </summary>
    public int Id { get; init; }

    /// <summary> Идентификатор определения функции. </summary>
    public int FeatureDefinitionId { get; init; }

    /// <summary> Код функции. </summary>
    public string FeatureCode { get; init; } = string.Empty;

    /// <summary> Название функции. </summary>
    public string FeatureName { get; init; } = string.Empty;

    /// <summary> Тип значения функции. </summary>
    public string ValueType { get; init; } = string.Empty;

    /// <summary> Логическое значение функции. </summary>
    public bool? BoolValue { get; init; }

    /// <summary> Целочисленное значение функции. </summary>
    public int? IntValue { get; init; }

    /// <summary> Признак включённой функции. </summary>
    public bool IsEnabled { get; init; }

    /// <summary> Дата создания предоставления. </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary> Дата начала действия. </summary>
    public DateTime? StartDate { get; init; }

    /// <summary> Дата окончания действия. </summary>
    public DateTime? EndDate { get; init; }

    /// <summary> Источник предоставления. </summary>
    public string Source { get; init; } = string.Empty;

    /// <summary> Комментарий к предоставлению. </summary>
    public string? Comment { get; init; }

    /// <summary> Идентификатор пользователя, выдавшего функцию. </summary>
    public string? GrantedByUserId { get; init; }

    /// <summary> Признак активного предоставления. </summary>
    public bool IsActive { get; init; }
}
