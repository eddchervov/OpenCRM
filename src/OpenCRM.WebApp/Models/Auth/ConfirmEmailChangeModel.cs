namespace OpenCRM.WebApp.Models.Auth;

#nullable disable

/// <summary> Модель страницы подтверждения смены email. </summary>
public class ConfirmEmailChangeModel
{
    /// <summary> Успешно ли подтверждена смена email. </summary>
    public bool Succeeded { get; set; }

    /// <summary> Сообщение о результате подтверждения смены email. </summary>
    public string StatusMessage { get; set; }
}

