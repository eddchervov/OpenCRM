namespace OpenCRM.WebApp.Models.Auth;

#nullable disable

/// <summary> Модель страницы подтверждения email. </summary>
public class ConfirmEmailModel
{
    /// <summary> Сообщение о результате подтверждения email. </summary>
    public string StatusMessage { get; set; }

    /// <summary> Признак успешного подтверждения email. </summary>
    public bool Succeeded { get; set; }
}

