using System.ComponentModel.DataAnnotations;
using OpenCRM.Modules.Notifications.Emails;

#nullable disable

namespace OpenCRM.WebApp.Models.Auth;

/// <summary> Модель входа по коду, отправленному на email. </summary>
public class EmailCodeLoginModel
{
    /// <summary> Email пользователя. </summary>
    [Required(ErrorMessage = "Укажите email")]
    [StringLength(100, ErrorMessage = "Длина {0} должна быть не менее {2} и не более {1} символов.", MinimumLength = 7)]
    [StrictEmailAddress]
    public string Email { get; set; }

    /// <summary> Код подтверждения из письма. </summary>
    [RegularExpression(@"^\d{8}$", ErrorMessage = "Код должен состоять из 8 цифр")]
    public string Code { get; set; }

    /// <summary> Адрес возврата после входа. </summary>
    public string ReturnUrl { get; set; }

    /// <summary> Признак отправки кода на email. </summary>
    public bool CodeSent { get; set; }

    /// <summary> Признак необходимости согласий при регистрации. </summary>
    public bool RequiresRegistrationConsent { get; set; }

    /// <summary> Согласие на обработку персональных данных. </summary>
    public bool PersonalDataProcessingConsent { get; set; }

    /// <summary> Согласие на получение рекламных сообщений. </summary>
    public bool AdvertisingConsent { get; set; }

    /// <summary> Сообщение о результате входа. </summary>
    public string StatusMessage { get; set; }
}
