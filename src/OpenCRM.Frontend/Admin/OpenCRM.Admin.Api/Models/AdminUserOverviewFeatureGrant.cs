namespace OpenCRM.Admin.Api.Models;

/// <summary> Выданная пользователю возможность в обзоре. </summary>
public sealed class AdminUserOverviewFeatureGrant
{
    /// <summary> Идентификатор выдачи возможности. </summary>
    public int Id { get; init; }

    /// <summary> Идентификатор определения возможности. </summary>
    public int FeatureDefinitionId { get; init; }

    /// <summary> Код возможности. </summary>
    public string FeatureCode { get; init; } = string.Empty;

    /// <summary> Название возможности. </summary>
    public string FeatureName { get; init; } = string.Empty;

    /// <summary> Тип значения возможности. </summary>
    public string ValueType { get; init; } = string.Empty;

    /// <summary> Логическое значение возможности. </summary>
    public bool? BoolValue { get; init; }

    /// <summary> Целочисленное значение возможности. </summary>
    public int? IntValue { get; init; }

    /// <summary> Признак включённой возможности. </summary>
    public bool IsEnabled { get; init; }

    /// <summary> Время создания выдачи. </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary> Дата начала действия возможности. </summary>
    public DateTime? StartDate { get; init; }

    /// <summary> Дата окончания действия возможности. </summary>
    public DateTime? EndDate { get; init; }

    /// <summary> Источник выдачи возможности. </summary>
    public string Source { get; init; } = string.Empty;

    /// <summary> Комментарий к выдаче. </summary>
    public string? Comment { get; init; }

    /// <summary> Идентификатор пользователя, выдавшего возможность. </summary>
    public string? GrantedByUserId { get; init; }

    /// <summary> Признак активной выдачи. </summary>
    public bool IsActive { get; init; }
}
