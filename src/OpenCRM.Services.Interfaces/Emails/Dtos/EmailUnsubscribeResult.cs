namespace OpenCRM.Services.Interfaces.Emails.Dtos;

/// <summary> Результат отписки от email-рассылки. </summary>
public enum EmailUnsubscribeResult
{
    /// <summary> Некорректная ссылка отписки. </summary>
    InvalidLink,

    /// <summary> Подписка не найдена. </summary>
    SubscriptionNotFound,

    /// <summary> Отписка выполнена. </summary>
    Unsubscribed
}
