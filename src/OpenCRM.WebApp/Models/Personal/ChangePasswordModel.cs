using System.ComponentModel.DataAnnotations;

namespace OpenCRM.WebApp.Models.Personal;

#nullable disable

/// <summary> Модель смены пароля пользователя. </summary>
public class ChangePasswordModel
{
    /// <summary> Текущий пароль. </summary>
    [DataType(DataType.Password)]
    [Display(Name = "Текущий пароль")]
    [Required(ErrorMessage = "Укажите текущий пароль.")]
    public string OldPassword { get; set; }

    /// <summary> Новый пароль. </summary>
    [Required(ErrorMessage = "Укажите новый пароль.")]
    [StringLength(100, ErrorMessage = "Длина пароля должна быть не менее {2} и не более {1} символов.", MinimumLength = 8)]
    [DataType(DataType.Password)]
    [Display(Name = "Новый пароль")]
    public string NewPassword { get; set; }

    /// <summary> Подтверждение нового пароля. </summary>
    [DataType(DataType.Password)]
    [Display(Name = "Подтверждение нового пароля")]
    [Required(ErrorMessage = "Подтвердите новый пароль.")]
    [Compare("NewPassword", ErrorMessage = "Пароль и подтверждение пароля не совпадают.")]
    public string ConfirmPassword { get; set; }

    /// <summary> Сообщение о результате смены пароля. </summary>
    public string StatusMessage { get; set; }
}

