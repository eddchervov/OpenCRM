namespace OpenCRM.Services.Interfaces.Home.Dtos;

/// <summary> Результат отправки ответа на обращение поддержки. </summary>
public enum SupportMessageReplyResult
{
    /// <summary> Ответ отправлен успешно. </summary>
    Succeeded,

    /// <summary> Обращение не найдено. </summary>
    NotFound,

    /// <summary> Нет прав на отправку ответа. </summary>
    Forbidden,

    /// <summary> Текст ответа некорректен. </summary>
    InvalidText
}
