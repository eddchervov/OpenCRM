using OpenCRM.Domain.Identity;

namespace OpenCRM.Domain;

/// <summary> Выданная пользователю возможность. </summary>
public class UserFeatureGrant
{
    /// <summary> Идентификатор записи о выдаче возможности. </summary>
    public int Id { get; set; }

    /// <summary> Идентификатор пользователя. </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary> Пользователь, которому выдана возможность. </summary>
    public ApplicationUser User { get; set; } = null!;

    /// <summary> Идентификатор определения возможности. </summary>
    public int FeatureDefinitionId { get; set; }

    /// <summary> Определение выданной возможности. </summary>
    public FeatureDefinition FeatureDefinition { get; set; } = null!;

    /// <summary> Логическое значение выданной возможности. </summary>
    public bool? BoolValue { get; set; }

    /// <summary> Целочисленное значение выданной возможности. </summary>
    public int? IntValue { get; set; }

    /// <summary> Признак активности выданной возможности. </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary> Дата и время создания записи о выдаче. </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary> Дата начала действия выданной возможности. </summary>
    public DateTime? StartDate { get; set; }

    /// <summary> Дата окончания действия выданной возможности. </summary>
    public DateTime? EndDate { get; set; }

    /// <summary> Источник выдачи возможности. </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary> Комментарий к выдаче возможности. </summary>
    public string? Comment { get; set; }

    /// <summary> Идентификатор пользователя, выдавшего возможность. </summary>
    public string? GrantedByUserId { get; set; }
}

