using System.ComponentModel.DataAnnotations;

namespace OpenCRM.Modules.Organizations.Contracts.Dtos;

/// <summary> Данные для обновления реквизитов организации. </summary>
public class UpdateOrganizationRequisitesDto
{
    /// <summary> Юридическое наименование организации. </summary>
    [MaxLength(240, ErrorMessage = "Не более 240 символов.")]
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
    [MaxLength(400, ErrorMessage = "Не более 400 символов.")]
    public string? LegalAddress { get; set; }

    /// <summary> Фактический адрес организации. </summary>
    [MaxLength(400, ErrorMessage = "Не более 400 символов.")]
    public string? ActualAddress { get; set; }

    /// <summary> Телефон организации. </summary>
    [MaxLength(40, ErrorMessage = "Не более 40 символов.")]
    public string? Phone { get; set; }

    /// <summary> Email организации. </summary>
    [EmailAddress(ErrorMessage = "Укажите корректный адрес электронной почты.")]
    [MaxLength(320, ErrorMessage = "Не более 320 символов.")]
    public string? Email { get; set; }

    /// <summary> Должность подписанта. </summary>
    [MaxLength(120, ErrorMessage = "Не более 120 символов.")]
    public string? SignatoryPosition { get; set; }

    /// <summary> Имя подписанта. </summary>
    [MaxLength(160, ErrorMessage = "Не более 160 символов.")]
    public string? SignatoryName { get; set; }

    /// <summary> Основание полномочий подписанта. </summary>
    [MaxLength(160, ErrorMessage = "Не более 160 символов.")]
    public string? SignatoryBasis { get; set; }
}
