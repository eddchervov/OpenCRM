using OpenCRM.Domain;
using OpenCRM.Modules.Notifications.Emails;
using System.ComponentModel.DataAnnotations;

#nullable disable

namespace OpenCRM.WebApp.Models.Auth;

/// <summary> Модель регистрации нового пользователя. </summary>
public class RegisterModel
{
    /// <summary> Email нового пользователя. </summary>
    [Required(ErrorMessage = "Поле Email обязательно для заполнения")]
    [StringLength(100, ErrorMessage = "Длина {0} должна быть не менее {2} и не более {1} символов.", MinimumLength = 7)]
    [StrictEmailAddress]
    public string Email { get; set; }

    //[Display(Name = "Номер телефона")]
    //public string Phone { get; set; }

    /// <summary> Пароль нового пользователя. </summary>
    [Required(ErrorMessage = "Пароль обязателен для заполнения")]
    [StringLength(100, ErrorMessage = "Длина {0} должна быть не менее {2} и не более {1} символов.", MinimumLength = 8)]
    [DataType(DataType.Password)]
    [Display(Name = "Пароль")]
    public string Password { get; set; }

    //[Required(ErrorMessage = "Подтверждение пароля обязательно для заполнения")]
    //[DataType(DataType.Password)]
    //[Display(Name = "Подтверждение пароля")]
    //[Compare("Password", ErrorMessage = "Пароль и подтверждение пароля не совпадают.")]
    //public string ConfirmPassword { get; set; }

    /// <summary> Адрес возврата после регистрации. </summary>
    public string ReturnUrl { get; set; }

    /// <summary> Согласие на обработку персональных данных. </summary>
    public bool PersonalDataProcessingConsent { get; set; }

    /// <summary> Согласие на получение рекламных сообщений. </summary>
    public bool AdvertisingConsent { get; set; }

}
