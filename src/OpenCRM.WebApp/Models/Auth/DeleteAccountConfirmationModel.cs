namespace OpenCRM.WebApp.Models.Auth;

/// <summary> Модель страницы подтверждения удаления аккаунта. </summary>
public class DeleteAccountConfirmationModel
{
    /// <summary> Сообщение для пользователя. </summary>
    public string? Message { get; set; }

    /// <summary> Признак того, что аккаунт был удалён. </summary>
    public bool WasDeleted { get; set; }

    /// <summary> Исходный email пользователя. </summary>
    public string? OriginalEmail { get; set; }
}

