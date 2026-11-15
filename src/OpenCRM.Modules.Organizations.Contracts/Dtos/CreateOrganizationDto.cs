using System.ComponentModel.DataAnnotations;

namespace OpenCRM.Modules.Organizations.Contracts.Dtos;

/// <summary> Данные для создания организации. </summary>
public class CreateOrganizationDto
{
    /// <summary> Наименование организации. </summary>
    [Required]
    [MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    /// <summary> Описание организации. </summary>
    [MaxLength(600)]
    public string? Description { get; set; }

    /// <summary> Код основного сценария организации. </summary>
    [Required]
    [MaxLength(80)]
    public string PrimaryScenarioCode { get; set; } = string.Empty;
}
