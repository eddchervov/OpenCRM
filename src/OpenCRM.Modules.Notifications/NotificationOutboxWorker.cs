using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OpenCRM.Modules.Notifications;

/// <summary> Фоновый сервис диспетчеризации исходящих уведомлений. </summary>
public sealed class NotificationOutboxWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> options,
    ILogger<NotificationOutboxWorker> logger) : BackgroundService
{
    /// <summary> Цикл фоновой диспетчеризации исходящих уведомлений. </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (true)
        {
            stoppingToken.ThrowIfCancellationRequested();

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<NotificationOutboxDispatcher>();
                while (await dispatcher.DispatchNextAsync(stoppingToken))
                {
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Не удалось передать уведомления из OpenCRM в RabbitMQ.");
            }

            await Task.Delay(TimeSpan.FromSeconds(options.Value.DispatchIntervalSeconds), stoppingToken);
        }
    }
}
