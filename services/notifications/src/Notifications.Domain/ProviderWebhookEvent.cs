namespace Notifications.Domain;

/// <summary>Сохранённое событие провайдера; идентификатор защищает от повторной обработки вебхука.</summary>
public sealed class ProviderWebhookEvent
{
    /// <summary> Идентификатор события вебхука. </summary>
    public Guid Id { get; set; }

    /// <summary> Идентификатор приложения, которому принадлежит событие. </summary>
    public string ApplicationId { get; set; } = string.Empty;

    /// <summary> Полезная нагрузка вебхука. </summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary> Время получения события. </summary>
    public DateTimeOffset ReceivedAt { get; set; }
}
