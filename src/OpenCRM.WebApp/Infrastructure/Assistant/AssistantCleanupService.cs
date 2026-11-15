using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain.Assistant;
using OpenCRM.Infrastructure.Data;

namespace OpenCRM.WebApp.Infrastructure.Assistant;

public sealed class AssistantCleanupService(IServiceScopeFactory scopes, AssistantOptions options, ILogger<AssistantCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(options.CleanupIntervalSeconds), stoppingToken);
                await using var scope = scopes.CreateAsyncScope();
                var context = scope.ServiceProvider.GetRequiredService<OpenCRMContext>();
                var now = DateTime.UtcNow;
                var stale = now.AddMinutes(-options.StaleReservationMinutes);
                await context.Set<AssistantUsage>().Where(item => item.Status == "reserved" && item.CompletedAt == null && item.CreatedAt < stale)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Status, "unknown")
                        .SetProperty(item => item.CompletedAt, now).SetProperty(item => item.ErrorCode, "provider_response_unknown"), stoppingToken);
                await context.Set<AssistantTelegramUpdate>().Where(item => item.Status == "processing" && item.StartedAt < stale)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Status, "failed")
                        .SetProperty(item => item.CompletedAt, now).SetProperty(item => item.ErrorCode, "execution_result_unknown"), stoppingToken);
                var retention = now.AddDays(-options.HistoryRetentionDays);
                await context.Set<AssistantTelegramUpdate>().Where(item => item.CreatedAt < retention && item.Status != "processing")
                    .ExecuteDeleteAsync(stoppingToken);
                await context.Set<AssistantTelegramLink>().Where(item => item.ExpiresAt < retention).ExecuteDeleteAsync(stoppingToken);
                var conversationRetention = now.AddDays(-options.ConversationHistoryRetentionDays);
                await context.Set<AssistantConversation>().Where(item => item.ExpiresAt < conversationRetention && (item.ProcessingUntil == null || item.ProcessingUntil < now))
                    .ExecuteDeleteAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Не удалось очистить временные данные помощника.");
            }
        }
    }
}
