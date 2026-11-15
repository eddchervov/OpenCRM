using System.ComponentModel.DataAnnotations;

#nullable disable

namespace OpenCRM.WebApp.Models.Auth;

/// <summary> Модель входа по email и паролю. </summary>
public class LoginModel
{
    /// <summary> Email пользователя. </summary>
    [Required(ErrorMessage = "Поле Email обязательно для заполнения")]
    [StringLength(100, ErrorMessage = "Длина {0} должна быть не менее {2} и не более {1} символов..", MinimumLength = 7)]
    [EmailAddress(ErrorMessage = "Поле Email не правильного формата")]
    public string Email { get; set; }

    /// <summary> Пароль пользователя. </summary>
    [Required(ErrorMessage = "Пароль обязателен для заполнения")]
    [StringLength(100, ErrorMessage = "Длина {0} должна быть не менее {2} и не более {1} символов..", MinimumLength = 6)]
    [DataType(DataType.Password)]
    [Display(Name = "Пароль")]
    public string Password { get; set; }

    /// <summary> Адрес возврата после входа. </summary>
    public string ReturnUrl { get; set; }

    /// <summary> Сообщение об ошибке входа. </summary>
    public string ErrorMessage { get; set; }

}
