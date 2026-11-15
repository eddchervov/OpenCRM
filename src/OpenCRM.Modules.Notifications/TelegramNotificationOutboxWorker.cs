using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;

namespace OpenCRM.Modules.Notifications;

/// <summary> Фоновый сервис диспетчеризации исходящих Telegram-уведомлений. </summary>
public sealed class TelegramNotificationOutboxWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> options,
    IConfiguration configuration,
    ILogger<TelegramNotificationOutboxWorker> logger) : BackgroundService
{
    /// <summary> Цикл фоновой диспетчеризации исходящих Telegram-уведомлений. </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (true)
        {
            stoppingToken.ThrowIfCancellationRequested();

            if (!configuration.GetValue<bool>("TelegramAdminNotifications:Enabled"))
            {
                await Task.Delay(TimeSpan.FromSeconds(options.Value.DispatchIntervalSeconds), stoppingToken);
                continue;
            }

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<TelegramNotificationOutboxDispatcher>();
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
                logger.LogError(exception, "Не удалось передать уведомление Telegram в RabbitMQ.");
            }

            await Task.Delay(TimeSpan.FromSeconds(options.Value.DispatchIntervalSeconds), stoppingToken);
        }
    }
}
