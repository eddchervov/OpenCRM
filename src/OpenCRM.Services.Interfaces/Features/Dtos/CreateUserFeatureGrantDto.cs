using System.ComponentModel.DataAnnotations;

namespace OpenCRM.Services.Interfaces.Features.Dtos;

/// <summary> Данные для выдачи пользователю права на функциональную возможность. </summary>
public class CreateUserFeatureGrantDto
{
    /// <summary> Идентификатор функциональной возможности. </summary>
    [Required]
    public int FeatureId { get; set; }

    /// <summary> Логическое значение права на возможность. </summary>
    public bool? BoolValue { get; set; }

    /// <summary> Целочисленное значение права на возможность. </summary>
    public int? IntValue { get; set; }

    /// <summary> Источник выдачи права. </summary>
    [Required]
    [StringLength(50)]
    public string Source { get; set; } = string.Empty;

    /// <summary> Дата начала действия права. </summary>
    public DateTime? StartDate { get; set; }

    /// <summary> Дата окончания действия права. </summary>
    public DateTime? EndDate { get; set; }

    /// <summary> Комментарий к праву. </summary>
    [StringLength(500)]
    public string? Comment { get; set; }
}

