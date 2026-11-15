using Microsoft.Extensions.Hosting;

namespace OpenCRM.WebApp.Infrastructure.Assistant.Telegram;

/// <summary>Получает сообщения тестового бота без публичного адреса локального сервера.</summary>
public sealed class AssistantTelegramPollingService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<AssistantTelegramPollingService> logger) : BackgroundService
{
    private readonly AssistantTelegramConfiguration _settings = new(configuration);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!this._settings.LongPollingEnabled || !this._settings.IsConfigured)
        {
            return;
        }
        if (!environment.IsEnvironment("AiPilot") || this._settings.LongPollingTimeoutSeconds <= 0 ||
            this._settings.LongPollingTimeoutSeconds >= this._settings.HttpTimeoutSeconds ||
            this._settings.LongPollingBatchSize is < 1 or > 100)
        {
            logger.LogError("Telegram long polling разрешён только для AiPilot с корректными ограничениями конфигурации.");

            return;
        }

        long? offset = null;
        var isValidated = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var client = scope.ServiceProvider.GetRequiredService<AssistantTelegramApiClient>();
                if (!isValidated)
                {
                    await client.ValidatePollingBotAsync(stoppingToken);
                    isValidated = true;
                    logger.LogInformation("Локальный Telegram-помощник @{BotUsername} подключён через long polling.", this._settings.BotUsername);
                }

                var updates = await client.GetUpdatesAsync(offset, stoppingToken);
                var service = scope.ServiceProvider.GetRequiredService<AssistantTelegramService>();
                foreach (var update in updates)
                {
                    if (!update.TryGetProperty("update_id", out var updateId) || !updateId.TryGetInt64(out var id) || id < 0 || id == long.MaxValue)
                    {
                        throw new InvalidOperationException("Telegram вернул некорректный идентификатор обновления.");
                    }
                    if (!await service.EnqueueWebhookAsync(update, this._settings.WebhookSecret, stoppingToken))
                    {
                        throw new InvalidOperationException("Не удалось принять обновление Telegram в локальную очередь.");
                    }

                    // Подтверждаем получение только после сохранения в общей очереди с защитой от повторов.
                    offset = id + 1;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                isValidated = false;
                logger.LogWarning("Ошибка локального приёма Telegram: {ErrorCode}, {ErrorSource}. Повтор после паузы.",
                    exception.GetType().Name, exception.TargetSite?.DeclaringType?.FullName);
                await Task.Delay(TimeSpan.FromSeconds(this._settings.PollingIntervalSeconds), stoppingToken);
            }
        }
    }
}
