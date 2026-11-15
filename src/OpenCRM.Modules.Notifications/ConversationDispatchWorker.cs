using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OpenCRM.Modules.Notifications;

/// <summary>Повторяет публикацию адресных chat-команд, пока брокер не подтвердит их приём.</summary>
public sealed class ConversationDispatchWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> options,
    IConfiguration configuration,
    ILogger<ConversationDispatchWorker> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!configuration.GetValue<bool>("TelegramChat:Enabled")
                && !configuration.GetValue<bool>("MaxChat:Enabled"))
            {
                await Task.Delay(TimeSpan.FromSeconds(options.Value.DispatchIntervalSeconds), stoppingToken);
                continue;
            }

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<ConversationDispatchDispatcher>();
                while (await dispatcher.DispatchPendingAsync(stoppingToken))
                {
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Не удалось передать сообщения переписки в RabbitMQ.");
            }

            await Task.Delay(TimeSpan.FromSeconds(options.Value.DispatchIntervalSeconds), stoppingToken);
        }
    }
}
