using System.ComponentModel.DataAnnotations;

namespace OpenCRM.Modules.Organizations.Contracts.Dtos;

/// <summary> Данные для обновления профиля организации. </summary>
public class UpdateOrganizationProfileDto
{
    /// <summary> Название организации. </summary>
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

    /// <summary> Код стиля документов организации. </summary>
    [MaxLength(40)]
    public string? DocumentStyleCode { get; set; } = "auto";

    /// <summary> Режим workflow обслуживания. </summary>
    [MaxLength(40)]
    public string? MaintenanceWorkflowMode { get; set; } = "staged";

    /// <summary> Разрешено ли сотрудникам самостоятельно брать работы. </summary>
    public bool AllowWorkSelfAssignment { get; set; }
}
