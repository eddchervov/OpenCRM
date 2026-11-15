using System.ComponentModel.DataAnnotations;

namespace OpenCRM.WebApp.Models.Auth;

/// <summary> Модель запроса на удаление аккаунта. </summary>
public class DeleteAccountModel
{
    /// <summary> Пароль для подтверждения удаления. </summary>
    [Required(ErrorMessage = "Пожалуйста, подтвердите пароль")]
    [DataType(DataType.Password)]
    [Display(Name = "Текущий пароль")]
    public string? Password { get; set; }

    /// <summary> Подтверждение согласия на удаление аккаунта. </summary>
    [Display(Name = "Я понимаю, что все мои данные будут удалены безвозвратно")]
    public bool ConfirmDeletion { get; set; }

    /// <summary> Признак наличия пароля у аккаунта. </summary>
    public bool HasPassword { get; set; }
}

