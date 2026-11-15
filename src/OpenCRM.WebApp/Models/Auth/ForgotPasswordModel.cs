using System.ComponentModel.DataAnnotations;

#nullable disable

namespace OpenCRM.WebApp.Models.Auth;

/// <summary> Модель запроса восстановления пароля. </summary>
public class ForgotPasswordModel
{
    /// <summary> Email для восстановления пароля. </summary>
    [Required(ErrorMessage = "Поле Email обязательно для заполнения")]
    [StringLength(100, ErrorMessage = "Длина {0} должна быть не менее {2} и не более {1} символов..", MinimumLength = 7)]
    [EmailAddress(ErrorMessage = "Поле Email не правильного формата")]
    public string Email { get; set; }

    /// <summary> Сообщение о результате запроса восстановления. </summary>
    public string StatusMessage { get; set; }
}

