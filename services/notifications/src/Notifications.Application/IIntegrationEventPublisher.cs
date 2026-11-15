namespace Notifications.Application;

/// <summary>Связывает исходящие сообщения с транзакцией сохранения данных.</summary>
public interface IIntegrationEventPublisher
{
    /// <summary>Добавляет внутреннее событие в transactional outbox.</summary>
    Task PublishAsync<T>(T message, CancellationToken cancellationToken) where T : class;

    /// <summary>Добавляет результат в адресную очередь приложения через transactional outbox.</summary>
    Task SendToApplicationAsync<T>(
        string applicationId,
        T message,
        CancellationToken cancellationToken) where T : class;
}
