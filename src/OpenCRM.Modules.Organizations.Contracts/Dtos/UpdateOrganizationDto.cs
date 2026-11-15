using System.ComponentModel.DataAnnotations;

namespace OpenCRM.Modules.Organizations.Contracts.Dtos;

/// <summary> Данные для полного обновления организации. </summary>
public class UpdateOrganizationDto
{
    /// <summary> Название организации. </summary>
    [Required]
    [MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    /// <summary> Описание организации. </summary>
    [MaxLength(600)]
    public string? Description { get; set; }

    /// <summary> Юридическое наименование организации. </summary>
    [MaxLength(240)]
    public string? LegalName { get; set; }

    /// <summary> ИНН организации. </summary>
    [RegularExpression(@"\A\s*(?:[0-9]{10}|[0-9]{12})?\s*\z", ErrorMessage = "ИНН должен содержать 10 или 12 цифр.")]
    public string? Inn { get; set; }

    /// <summary> КПП организации. </summary>
    [RegularExpression(@"\A\s*(?:[0-9]{4}[0-9A-Za-z]{2}[0-9]{3})?\s*\z", ErrorMessage = "КПП должен содержать 9 знаков: цифры, в 5–6-й позиции допустимы латинские буквы.")]
    public string? Kpp { get; set; }

    /// <summary> ОГРН организации. </summary>
    [RegularExpression(@"\A\s*(?:[0-9]{13}|[0-9]{15})?\s*\z", ErrorMessage = "ОГРН / ОГРНИП должен содержать 13 или 15 цифр.")]
    public string? Ogrn { get; set; }

    /// <summary> Юридический адрес организации. </summary>
    [MaxLength(400)]
    public string? LegalAddress { get; set; }

    /// <summary> Фактический адрес организации. </summary>
    [MaxLength(400)]
    public string? ActualAddress { get; set; }

    /// <summary> Телефон организации. </summary>
    [MaxLength(40)]
    public string? Phone { get; set; }

    /// <summary> Email организации. </summary>
    [EmailAddress]
    [MaxLength(320)]
    public string? Email { get; set; }

    /// <summary> Должность подписанта. </summary>
    [MaxLength(120)]
    public string? SignatoryPosition { get; set; }

    /// <summary> Имя подписанта. </summary>
    [MaxLength(160)]
    public string? SignatoryName { get; set; }

    /// <summary> Основание полномочий подписанта. </summary>
    [MaxLength(160)]
    public string? SignatoryBasis { get; set; }

    /// <summary> Код стиля документов организации. </summary>
    [MaxLength(40)]
    public string? DocumentStyleCode { get; set; }

    /// <summary> Режим workflow обслуживания. </summary>
    [MaxLength(40)]
    public string? MaintenanceWorkflowMode { get; set; }

    /// <summary> Код основного сценария организации. </summary>
    [Required]
    [MaxLength(80)]
    public string PrimaryScenarioCode { get; set; } = string.Empty;
}
