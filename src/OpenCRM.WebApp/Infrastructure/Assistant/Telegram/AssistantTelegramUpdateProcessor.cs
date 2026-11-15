using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace OpenCRM.WebApp.Infrastructure.Assistant.Telegram;

public sealed class AssistantTelegramUpdateProcessor(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<AssistantTelegramUpdateProcessor> logger) : BackgroundService
{
    private readonly AssistantTelegramConfiguration _settings = new(configuration);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<AssistantTelegramService>();
                var processed = await service.ProcessNextUpdateAsync(stoppingToken);
                if (!processed)
                {
                    await Task.Delay(TimeSpan.FromSeconds(this._settings.PollingIntervalSeconds), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError("Ошибка фоновой обработки очереди Telegram помощника: {ErrorCode}.", exception.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(this._settings.PollingIntervalSeconds), stoppingToken);
            }
        }
    }
}
