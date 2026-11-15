using System.ComponentModel.DataAnnotations;

namespace OpenCRM.Services.Interfaces.Features.Dtos;

/// <summary> Набор привязок функциональных возможностей к тарифному плану. </summary>
public class SavePlanFeaturesDto
{
    /// <summary> Список сохраняемых привязок возможностей. </summary>
    [Required]
    public List<SavePlanFeatureItemDto> Features { get; set; } = [];
}

