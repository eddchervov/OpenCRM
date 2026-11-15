namespace OpenCRM.WebApp.Models.Auth;

/// <summary> Данные обязательного экрана подтверждения email. </summary>
public sealed class EmailVerificationModel
{
    /// <summary> Текущий адрес аккаунта, требующий подтверждения. </summary>
    public string Email { get; init; } = string.Empty;

    /// <summary> Оставшееся время до повторной отправки. </summary>
    public int RetryAfterSeconds { get; init; }

    /// <summary> Сообщение об отправке письма. </summary>
    public string? StatusMessage { get; init; }

    /// <summary> Причина, по которой письмо не удалось отправить. </summary>
    public string? ErrorMessage { get; init; }
}
