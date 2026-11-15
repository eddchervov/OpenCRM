using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Notifications.Contracts;

namespace Notifications.Infrastructure;

/// <summary>Обрабатывает durable inbox ограниченными worker каждого подключённого канала.</summary>
public sealed partial class ConversationDeliveryInboxWorker(
    IServiceScopeFactory scopeFactory,
    IReadOnlyCollection<ConversationDeliveryProvider> providerCodes,
    IOptions<TelegramChatOptions> options,
    TimeProvider timeProvider,
    ILogger<ConversationDeliveryInboxWorker> logger) : BackgroundService
{
    private static readonly TimeSpan DiagnosticsInterval = TimeSpan.FromMinutes(1);

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = TimeSpan.FromSeconds(options.Value.InboxPollIntervalSeconds);
        var leaseDuration = TimeSpan.FromSeconds(options.Value.InboxLeaseSeconds);
        var providerWorkers = providerCodes
            .Select(providerCode => this.ProcessProviderAsync(providerCode, pollInterval, leaseDuration, stoppingToken));

        return Task.WhenAll(providerWorkers);
    }

    private async Task ProcessProviderAsync(
        ConversationDeliveryProvider providerCode,
        TimeSpan pollInterval,
        TimeSpan leaseDuration,
        CancellationToken stoppingToken)
    {
        var nextDiagnosticsAt = timeProvider.GetUtcNow();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var hasFullBatch = await this.ProcessBatchAsync(providerCode, leaseDuration, stoppingToken);
                if (timeProvider.GetUtcNow() >= nextDiagnosticsAt)
                {
                    await this.LogDiagnosticsAsync(providerCode, stoppingToken);
                    nextDiagnosticsAt = timeProvider.GetUtcNow().Add(DiagnosticsInterval);
                }

                if (hasFullBatch)
                {
                    continue;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogPollingFailure(logger, exception, providerCode);
            }

            try
            {
                await Task.Delay(pollInterval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task<bool> ProcessBatchAsync(
        ConversationDeliveryProvider providerCode,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Notifications.Domain.ConversationDeliveryInbox> messages;
        using (var scope = scopeFactory.CreateScope())
        {
            var inboxStore = scope.ServiceProvider.GetRequiredService<ConversationDeliveryInboxStore>();
            messages = await inboxStore.ClaimDueAsync(
                providerCode,
                options.Value.InboxWorkerConcurrency,
                leaseDuration,
                cancellationToken);
        }

        if (messages.Count == 0)
        {
            return false;
        }

        await Task.WhenAll(messages.Select(message => this.ProcessAsync(providerCode, message, cancellationToken)));

        return messages.Count == options.Value.InboxWorkerConcurrency;
    }

    private async Task ProcessAsync(
        ConversationDeliveryProvider providerCode,
        Notifications.Domain.ConversationDeliveryInbox message,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var inboxStore = scope.ServiceProvider.GetRequiredService<ConversationDeliveryInboxStore>();
        var processor = scope.ServiceProvider.GetServices<IConversationDeliveryProcessor>()
            .Single(candidate => candidate.ProviderCode == providerCode);
        var leaseId = message.LeaseId!.Value;

        try
        {
            await processor.ProcessAsync(
                new ConversationDeliveryWakeup(providerCode, message.MessageId, message.RequestId),
                cancellationToken);
            await inboxStore.CompleteAsync(message, leaseId, null, null, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            var retryAt = timeProvider.GetUtcNow().Add(GetRetryDelay(message.AttemptCount, exception));
            try
            {
                await inboxStore.CompleteAsync(
                    message,
                    leaseId,
                    retryAt,
                    GetErrorCode(exception),
                    cancellationToken);
            }
            catch (Exception persistenceException) when (!cancellationToken.IsCancellationRequested)
            {
                LogRescheduleFailure(logger, persistenceException, message.MessageId, processor.ProviderCode);

                return;
            }

            LogMessageRetry(logger, exception, message.MessageId, retryAt, processor.ProviderCode);
        }
    }

    private async Task LogDiagnosticsAsync(
        ConversationDeliveryProvider providerCode,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ConversationDeliveryInboxStore>();
        var snapshot = await store.GetSnapshotAsync(providerCode, cancellationToken);
        LogInboxSnapshot(
            logger,
            providerCode,
            snapshot.PendingCount,
            snapshot.DueCount,
            snapshot.ExpiredCount,
            snapshot.OldestPendingAt);
    }

    private static TimeSpan GetRetryDelay(int attemptCount, Exception exception)
    {
        if (exception is ConversationDeliveryDeferredException { RetryAfterSeconds: > 0 } deferred)
        {
            return TimeSpan.FromSeconds(Math.Clamp(deferred.RetryAfterSeconds.Value, 1, 900));
        }

        var exponentialSeconds = Math.Pow(2, Math.Clamp(attemptCount, 1, 10));

        return TimeSpan.FromSeconds(Math.Clamp(exponentialSeconds, 2, 900));
    }

    private static string GetErrorCode(Exception exception) => exception switch
    {
        ConversationDeliveryDeferredException => "delivery-deferred",
        HttpRequestException => "core-api-unavailable",
        TimeoutException => "delivery-timeout",
        _ => "delivery-processing-failed"
    };

    [LoggerMessage(Level = LogLevel.Error, Message = "Conversation delivery inbox polling failed for provider {ProviderCode}.")]
    private static partial void LogPollingFailure(
        ILogger logger,
        Exception exception,
        ConversationDeliveryProvider providerCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not reschedule conversation message {MessageId} for provider {ProviderCode}.")]
    private static partial void LogRescheduleFailure(
        ILogger logger,
        Exception exception,
        Guid messageId,
        ConversationDeliveryProvider providerCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Conversation message {MessageId} for provider {ProviderCode} will retry after {RetryAt}.")]
    private static partial void LogMessageRetry(
        ILogger logger,
        Exception exception,
        Guid messageId,
        DateTimeOffset retryAt,
        ConversationDeliveryProvider providerCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Conversation delivery inbox {ProviderCode}: {PendingCount} pending, {DueCount} due, {ExpiredCount} expired; oldest pending at {OldestPendingAt}.")]
    private static partial void LogInboxSnapshot(
        ILogger logger,
        ConversationDeliveryProvider providerCode,
        int pendingCount,
        int dueCount,
        int expiredCount,
        DateTimeOffset? oldestPendingAt);
}
