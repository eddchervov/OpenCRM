using System.ComponentModel.DataAnnotations;

namespace OpenCRM.Services.Interfaces.Features.Dtos;

/// <summary> Данные для сохранения привязки возможности к тарифному плану. </summary>
public class SavePlanFeatureItemDto
{
    /// <summary> Идентификатор функциональной возможности. </summary>
    [Required]
    public int FeatureId { get; set; }

    /// <summary> Признак привязки возможности к плану. </summary>
    public bool IsAssigned { get; set; }

    /// <summary> Логическое значение возможности в плане. </summary>
    public bool? BoolValue { get; set; }

    /// <summary> Целочисленное значение возможности в плане. </summary>
    public int? IntValue { get; set; }
}

