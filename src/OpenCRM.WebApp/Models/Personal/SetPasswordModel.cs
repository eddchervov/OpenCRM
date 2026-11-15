using System.ComponentModel.DataAnnotations;

namespace OpenCRM.WebApp.Models.Personal;

#nullable disable

/// <summary> Модель страницы установки пароля. </summary>
public class SetPasswordModel
{
    /// <summary> Новый пароль, вводимый пользователем. </summary>
    [Required(ErrorMessage = "Пароль обязателен для заполнения")]
    [StringLength(100, ErrorMessage = "Длина пароля должна быть не менее {2} и не более {1} символов.", MinimumLength = 8)]
    [DataType(DataType.Password)]
    [Display(Name = "Новый пароль")]
    public string NewPassword { get; set; }

    /// <summary> Повторный ввод нового пароля для подтверждения. </summary>
    [DataType(DataType.Password)]
    [Display(Name = "Подтверждение нового пароля")]
    [Compare("NewPassword", ErrorMessage = "Новый пароль и подтверждение пароля не совпадают.")]
    public string ConfirmPassword { get; set; }

    /// <summary> Сообщение о результате операции для отображения на странице. </summary>
    public string StatusMessage { get; set; }
}

