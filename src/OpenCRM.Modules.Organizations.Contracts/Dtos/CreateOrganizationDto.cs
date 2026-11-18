using System.ComponentModel.DataAnnotations;
using OpenCRM.Domain;

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

    /// <summary> Валюта учёта новой организации. </summary>
    [Required]
    [MaxLength(3)]
    public string CurrencyCode { get; set; } = CurrencyCodes.Rub;

    /// <summary> Часовой пояс организации в формате IANA. </summary>
    [Required]
    [MaxLength(100)]
    public string TimeZoneId { get; set; } = "Etc/UTC";

    /// <summary> Код основного сценария организации. </summary>
    [Required]
    [MaxLength(80)]
    public string PrimaryScenarioCode { get; set; } = string.Empty;
}
