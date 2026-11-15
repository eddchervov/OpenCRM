namespace Notifications.Infrastructure;

/// <summary>Распределённо ограничивает запросы и диалоги MAX.</summary>
public interface IMaxConversationRateLimiter
{
    /// <summary>Резервирует интервал для диалога между экземплярами сервиса.</summary>
    Task<IAsyncDisposable> AcquireConversationAsync(string key, CancellationToken cancellationToken);

    /// <summary>Резервирует один запрос к MAX API в общем лимите экземпляров.</summary>
    Task WaitForGlobalRequestSlotAsync(CancellationToken cancellationToken);
}
