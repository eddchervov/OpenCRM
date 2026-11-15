using System.ComponentModel.DataAnnotations;

#nullable disable

namespace OpenCRM.WebApp.Models.Auth;

/// <summary> Модель установки нового пароля. </summary>
public class ResetPasswordModel
{
    /// <summary> Email пользователя. </summary>
    [Required(ErrorMessage = "Поле Email обязательно для заполнения")]
    [StringLength(100, ErrorMessage = "Длина {0} должна быть не менее {2} и не более {1} символов..", MinimumLength = 7)]
    [EmailAddress(ErrorMessage = "Поле Email не правильного формата")]
    public string Email { get; set; }

    /// <summary> Новый пароль. </summary>
    [Required(ErrorMessage = "Пароль обязателен для заполнения")]
    [StringLength(100, ErrorMessage = "Длина {0} должна быть не менее {2} и не более {1} символов.", MinimumLength = 8)]
    [DataType(DataType.Password)]
    [Display(Name = "Пароль")]
    public string Password { get; set; }

    /// <summary> Повторный ввод нового пароля. </summary>
    [Required(ErrorMessage = "Подтверждение пароля обязательно для заполнения")]
    [DataType(DataType.Password)]
    [Display(Name = "Подтверждение пароля")]
    [Compare("Password", ErrorMessage = "Пароль и подтверждение пароля не совпадают.")]
    public string ConfirmPassword { get; set; }

    /// <summary> Идентификатор пользователя. </summary>
    [Required]
    public string UserId { get; set; }

    /// <summary> Код подтверждения сброса пароля. </summary>
    [Required]
    public string Code { get; set; }

    /// <summary> Сообщение о результате сброса пароля. </summary>
    public string? StatusMessage { get; set; }
}

