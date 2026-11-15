using Microsoft.EntityFrameworkCore;
using Notifications.Domain;

namespace Notifications.Infrastructure;

/// <summary>Идемпотентно фиксирует внешнюю попытку доставки на время действия lease.</summary>
public sealed class ConversationDeliveryAttemptStore(NotificationDbContext context)
{
    /// <summary>Возвращает состояние lease или создаёт его до любых внешних действий.</summary>
    public async Task<TelegramChatDeliveryState> BeginAsync(
        Guid leaseId,
        Guid requestId,
        Guid messageId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO notifications."TelegramChatDeliveryStates"
                ("LeaseId", "RequestId", "MessageId", "Status", "SafeToRetry", "UpdatedAt")
            VALUES ({leaseId}, {requestId}, {messageId}, 'ready', FALSE, {DateTimeOffset.UtcNow})
            ON CONFLICT ("LeaseId") DO NOTHING
            """, cancellationToken);
        var state = await context.TelegramChatDeliveryStates
            .FromSqlInterpolated($"""
                SELECT * FROM notifications."TelegramChatDeliveryStates"
                WHERE "LeaseId" = {leaseId}
                FOR UPDATE
                """)
            .AsNoTracking()
            .SingleAsync(cancellationToken);
        if (state.RequestId != requestId || state.MessageId != messageId)
        {
            throw new InvalidOperationException("Telegram lease ID was reused for a different message.");
        }

        await transaction.CommitAsync(cancellationToken);

        return state;
    }

    /// <summary>Атомарно допускает к provider вызову только один обработчик одного lease.</summary>
    public async Task<bool> TryMarkProviderStartedAsync(Guid leaseId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var updatedRows = await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE notifications."TelegramChatDeliveryStates"
            SET "Status" = 'started', "StartedAt" = {now}, "UpdatedAt" = {now}
            WHERE "LeaseId" = {leaseId} AND "Status" = 'ready'
            """, cancellationToken);

        return updatedRows == 1;
    }

    /// <summary>Сохраняет итог provider call до подтверждения результата core API.</summary>
    public async Task<bool> SaveResultAsync(
        Guid leaseId,
        string status,
        string? providerMessageId,
        string? errorCode,
        string? errorMessage,
        bool safeToRetry,
        int? retryAfterSeconds,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        DateTimeOffset? retryAfterAt = retryAfterSeconds is > 0
            ? now.AddSeconds(Math.Min(retryAfterSeconds.Value, 86400))
            : null;
        var updatedRows = await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE notifications."TelegramChatDeliveryStates"
            SET "Status" = {status},
                "ProviderMessageId" = {providerMessageId},
                "ErrorCode" = {errorCode},
                "ErrorMessage" = {errorMessage},
                "SafeToRetry" = {safeToRetry},
                "UpdatedAt" = {now},
                "RetryAfterAt" = {retryAfterAt}
            WHERE "LeaseId" = {leaseId} AND "Status" IN ('ready', 'started')
            """, cancellationToken);

        return updatedRows == 1;
    }

    /// <summary>Читает сохранённый исход, если конкурентный результат уже завершил lease.</summary>
    public Task<TelegramChatDeliveryState> GetAsync(Guid leaseId, CancellationToken cancellationToken) =>
        context.TelegramChatDeliveryStates.AsNoTracking()
            .SingleAsync(item => item.LeaseId == leaseId, cancellationToken);
}
