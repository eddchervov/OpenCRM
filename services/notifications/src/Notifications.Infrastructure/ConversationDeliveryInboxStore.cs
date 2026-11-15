using Microsoft.EntityFrameworkCore;
using Notifications.Contracts;
using Notifications.Domain;

namespace Notifications.Infrastructure;

/// <summary>Принимает, арендует и повторяет адресные команды доставки переписок.</summary>
public sealed class ConversationDeliveryInboxStore(
    NotificationDbContext context,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan MaxMessageAge = TimeSpan.FromHours(24);

    /// <summary>Сохраняет команду до подтверждения сообщения RabbitMQ.</summary>
    public async Task EnqueueAsync(
        ConversationDeliveryProvider providerCode,
        Guid messageId,
        Guid requestId,
        CancellationToken cancellationToken)
    {
        if (messageId == Guid.Empty || requestId == Guid.Empty)
        {
            throw new ArgumentException("Conversation delivery requires message and request IDs.");
        }

        var provider = GetProviderSegment(providerCode);
        var now = timeProvider.GetUtcNow();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO notifications."ConversationDeliveryInbox"
                ("ProviderCode", "MessageId", "RequestId", "Status", "NextAttemptAt", "AttemptCount", "CreatedAt", "UpdatedAt")
            VALUES ({provider}, {messageId}, {requestId}, 'pending', {now}, 0, {now}, {now})
            ON CONFLICT ("ProviderCode", "MessageId", "RequestId") DO NOTHING
            """, cancellationToken);
    }

    /// <summary>Захватывает пакет одного провайдера, исключая параллельную обработку.</summary>
    public async Task<IReadOnlyList<ConversationDeliveryInbox>> ClaimDueAsync(
        ConversationDeliveryProvider providerCode,
        int batchSize,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        if (batchSize is < 1 or > 16)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        }

        var provider = GetProviderSegment(providerCode);
        var now = timeProvider.GetUtcNow();
        var leaseExpiresAt = now.Add(leaseDuration);
        var executionStrategy = context.Database.CreateExecutionStrategy();

        return await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE notifications."ConversationDeliveryInbox"
                SET "Status" = 'expired', "LeaseId" = NULL, "LeaseExpiresAt" = NULL,
                    "CompletedAt" = {now}, "UpdatedAt" = {now}
                WHERE "ProviderCode" = {provider} AND "Status" = 'pending' AND "CreatedAt" <= {now - MaxMessageAge}
                """, cancellationToken);

            var dueMessages = await context.ConversationDeliveryInbox
                .FromSqlInterpolated($"""
                    SELECT * FROM notifications."ConversationDeliveryInbox"
                    WHERE "ProviderCode" = {provider} AND "Status" = 'pending'
                        AND "NextAttemptAt" <= {now}
                        AND ("LeaseExpiresAt" IS NULL OR "LeaseExpiresAt" <= {now})
                    ORDER BY "NextAttemptAt", "CreatedAt"
                    LIMIT {batchSize}
                    FOR UPDATE SKIP LOCKED
                    """)
                .ToListAsync(cancellationToken);

            foreach (var message in dueMessages)
            {
                message.LeaseId = Guid.NewGuid();
                message.LeaseExpiresAt = leaseExpiresAt;
                message.AttemptCount++;
                message.UpdatedAt = now;
            }

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return dueMessages;
        });
    }

    /// <summary>Завершает аренду либо возвращает команду на ограниченный повтор.</summary>
    public async Task CompleteAsync(
        ConversationDeliveryInbox message,
        Guid leaseId,
        DateTimeOffset? retryAt,
        string? errorCode,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var status = retryAt is null
            ? "completed"
            : message.CreatedAt <= now - MaxMessageAge || retryAt > message.CreatedAt + MaxMessageAge
                ? "expired"
                : "pending";
        var nextAttemptAt = retryAt ?? now;
        DateTimeOffset? completedAt = status is "completed" or "expired" ? now : null;
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE notifications."ConversationDeliveryInbox"
            SET "Status" = {status}, "NextAttemptAt" = {nextAttemptAt},
                "LeaseId" = NULL, "LeaseExpiresAt" = NULL,
                "LastErrorCode" = {errorCode}, "CompletedAt" = {completedAt}, "UpdatedAt" = {now}
            WHERE "ProviderCode" = {message.ProviderCode}
                AND "MessageId" = {message.MessageId} AND "RequestId" = {message.RequestId}
                AND "Status" = 'pending' AND "LeaseId" = {leaseId}
            """, cancellationToken);
    }

    /// <summary>Возвращает компактный снимок очереди одного провайдера.</summary>
    public async Task<ConversationDeliveryInboxSnapshot> GetSnapshotAsync(
        ConversationDeliveryProvider providerCode,
        CancellationToken cancellationToken)
    {
        var provider = GetProviderSegment(providerCode);
        var now = timeProvider.GetUtcNow();
        var pending = context.ConversationDeliveryInbox.AsNoTracking()
            .Where(item => item.ProviderCode == provider && item.Status == "pending");
        var pendingCount = await pending.CountAsync(cancellationToken);
        var dueCount = await pending.CountAsync(item => item.NextAttemptAt <= now, cancellationToken);
        var expiredCount = await context.ConversationDeliveryInbox.AsNoTracking()
            .CountAsync(item => item.ProviderCode == provider && item.Status == "expired", cancellationToken);
        var oldestPendingAt = await pending
            .MinAsync(item => (DateTimeOffset?)item.CreatedAt, cancellationToken);

        return new ConversationDeliveryInboxSnapshot(pendingCount, dueCount, expiredCount, oldestPendingAt);
    }

    private static string GetProviderSegment(ConversationDeliveryProvider providerCode) => providerCode switch
    {
        ConversationDeliveryProvider.Telegram => "telegram",
        ConversationDeliveryProvider.Max => "max",
        _ => throw new ArgumentOutOfRangeException(nameof(providerCode))
    };
}
