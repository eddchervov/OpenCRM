namespace OpenCRM.Services.Interfaces.Features.Dtos;

/// <summary> Выданное пользователю право на функциональную возможность. </summary>
public class UserFeatureGrantDto
{
    /// <summary> Идентификатор права. </summary>
    public int Id { get; set; }

    /// <summary> Идентификатор функциональной возможности. </summary>
    public int FeatureId { get; set; }

    /// <summary> Код функциональной возможности. </summary>
    public string FeatureCode { get; set; } = string.Empty;

    /// <summary> Наименование функциональной возможности. </summary>
    public string FeatureName { get; set; } = string.Empty;

    /// <summary> Тип значения права. </summary>
    public string ValueType { get; set; } = string.Empty;

    /// <summary> Логическое значение права. </summary>
    public bool? BoolValue { get; set; }

    /// <summary> Целочисленное значение права. </summary>
    public int? IntValue { get; set; }

    /// <summary> Признак активности права. </summary>
    public bool IsEnabled { get; set; }

    /// <summary> Дата и время создания права. </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary> Дата начала действия права. </summary>
    public DateTime? StartDate { get; set; }

    /// <summary> Дата окончания действия права. </summary>
    public DateTime? EndDate { get; set; }

    /// <summary> Источник выдачи права. </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary> Комментарий к праву. </summary>
    public string? Comment { get; set; }

    /// <summary> Идентификатор пользователя, выдавшего право. </summary>
    public string? GrantedByUserId { get; set; }
}

