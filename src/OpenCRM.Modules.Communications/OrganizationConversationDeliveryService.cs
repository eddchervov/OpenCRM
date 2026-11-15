using System.Data;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Communications.Contracts;
using OpenCRM.Modules.Communications.Contracts.Dtos;

namespace OpenCRM.Modules.Communications;

internal sealed class OrganizationConversationDeliveryService(
    OpenCRMContext context,
    ConversationAccess access,
    ConversationRouteAvailability routeAvailability) : IConversationDeliveryOutbox, IConversationDispatchOutbox
{
    private const string Outgoing = "outgoing";
    private const string Queued = "queued";
    private const string Sending = "sending";
    private const string Accepted = "accepted";
    private const string Failed = "failed";
    private const string Uncertain = "uncertain";
    private const string Cancelled = "cancelled";
    private const string Telegram = "telegram";
    private const int DispatchBatchMaximum = 16;
    private static readonly TimeSpan s_queuedMessageMaximumAge = TimeSpan.FromHours(24);
    private static readonly TimeSpan s_dispatchRecoveryDelay = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan s_dispatchMaximumRetryDelay = TimeSpan.FromHours(1);

    public Task<ConversationDeliveryLeaseDto?> ClaimByMessageIdAsync(
        Guid messageId,
        Guid requestId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default) =>
        ExecuteWithExecutionStrategyAsync(() => ClaimByMessageIdCoreAsync(messageId, requestId, leaseDuration, cancellationToken));

    private async Task<ConversationDeliveryLeaseDto?> ClaimByMessageIdCoreAsync(
        Guid messageId,
        Guid requestId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var conversationId = await context.OrganizationConversationMessages.AsNoTracking()
            .Where(item => item.Id == messageId)
            .Select(item => item.ConversationId)
            .SingleOrDefaultAsync(cancellationToken);
        if (conversationId == Guid.Empty)
        {
            return null;
        }

        var conversation = await context.OrganizationConversations
            .FromSqlInterpolated($"SELECT * FROM \"OrganizationConversations\" WHERE \"Id\" = {conversationId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (conversation is null)
        {
            return null;
        }

        var message = await context.OrganizationConversationMessages
            .FromSqlInterpolated($"SELECT * FROM \"OrganizationConversationMessages\" WHERE \"Id\" = {messageId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (message is null || message.Direction != Outgoing || message.RequestId != requestId)
        {
            return null;
        }

        if (!await access.HasClientMessagingAccessForOrganizationAsync(conversation.OrganizationId, cancellationToken))
        {
            message.Status = Cancelled;
            message.SafeToRetry = false;
            message.RetryAt = null;
            message.ErrorCode = "client-messaging-pro-required";
            message.ErrorMessage = "Отправка сообщения приостановлена: подключите подписку PRO.";
            conversation.UpdatedAt = NextRevision(conversation.UpdatedAt);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return null;
        }

        var now = DateTime.UtcNow;
        if (message.CreatedAt <= now.Subtract(s_queuedMessageMaximumAge)
            && (message.Status == Queued || (message.Status == Failed && message.SafeToRetry)))
        {
            message.Status = Failed;
            message.SafeToRetry = false;
            message.RetryAt = null;
            message.ErrorCode = "delivery-expired";
            message.ErrorMessage = "Сообщение не было доставлено в течение 24 часов.";
            conversation.UpdatedAt = NextRevision(conversation.UpdatedAt);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return null;
        }

        var isRecovery = message.Status == Sending && message.LeaseExpiresAt <= now;
        if (!isRecovery && message.Status == Sending)
        {
            throw new ConversationDeliveryDeferredException(
                "delivery-in-progress",
                (message.LeaseExpiresAt ?? now.AddMinutes(5)) - now);
        }

        if (!isRecovery && message.Status == Failed && message.SafeToRetry && message.RetryAt > now)
        {
            throw new ConversationDeliveryDeferredException("delivery-retry-scheduled", message.RetryAt.Value - now);
        }

        if (!isRecovery && message.Status != Queued && !(message.Status == Failed && message.SafeToRetry))
        {
            return null;
        }

        var blockingMessage = await context.OrganizationConversationMessages.AsNoTracking()
            .Where(item => item.ConversationId == conversationId
                && item.Direction == Outgoing
                && item.Sequence < message.Sequence
                && (item.Status == Queued || item.Status == Sending
                    || (item.Status == Failed && item.SafeToRetry)))
            .OrderBy(item => item.Sequence)
            .Select(item => new { item.Status, item.RetryAt, item.LeaseExpiresAt })
            .FirstOrDefaultAsync(cancellationToken);
        if (blockingMessage is not null)
        {
            var retryAt = blockingMessage.Status == Failed
                ? blockingMessage.RetryAt
                : blockingMessage.Status == Sending
                    ? blockingMessage.LeaseExpiresAt
                    : null;
            var retryAfter = retryAt.HasValue
                ? retryAt.Value - DateTime.UtcNow
                : TimeSpan.FromMinutes(5);
            throw new ConversationDeliveryDeferredException(
                "earlier-delivery-pending",
                retryAfter);
        }

        if (!isRecovery && message.DeliveryAttemptCount >= 5)
        {
            message.Status = Failed;
            message.SafeToRetry = false;
            message.RetryAt = null;
            conversation.UpdatedAt = NextRevision(conversation.UpdatedAt);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return null;
        }

        if (conversation.IsReadOnly || !conversation.IsChatActive
            || !await routeAvailability.IsCurrentAsync(conversation, cancellationToken))
        {
            message.Status = Cancelled;
            message.SafeToRetry = false;
            message.RetryAt = null;
            conversation.UpdatedAt = NextRevision(conversation.UpdatedAt);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return null;
        }

        message.Status = Sending;
        if (!isRecovery)
        {
            message.LeaseId = Guid.NewGuid();
            message.DeliveryAttemptCount++;
        }

        var leaseId = message.LeaseId ?? throw new InvalidOperationException("У отправляемого сообщения отсутствует идентификатор аренды.");
        message.LeaseExpiresAt = now.Add(ClampLease(leaseDuration));
        message.SafeToRetry = false;
        message.RetryAt = null;
        message.SentAt ??= now;
        conversation.UpdatedAt = NextRevision(conversation.UpdatedAt);
        await context.OrganizationConversationAttachments
            .Where(item => item.MessageId == message.Id)
            .OrderBy(item => item.SortOrder)
            .LoadAsync(cancellationToken);
        var channel = conversation.ConnectionId.HasValue
            ? await context.OrganizationChannelConnections.AsNoTracking()
                .Where(item => item.Id == conversation.ConnectionId.Value && item.OrganizationId == conversation.OrganizationId)
                .Select(item => new { item.Id, item.ProviderCode, item.Username })
                .FirstOrDefaultAsync(cancellationToken)
            : null;
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ConversationDeliveryLeaseDto
        {
            MessageId = message.Id,
            LeaseId = leaseId,
            IsRecovery = isRecovery,
            OrganizationId = conversation.OrganizationId,
            BotId = conversation.BotId,
            ChatId = conversation.TelegramChatId,
            ProviderCode = channel?.ProviderCode ?? Telegram,
            ConnectionId = channel?.Id ?? Guid.Empty,
            ExternalConversationId = conversation.ExternalConversationId
                ?? conversation.TelegramChatId?.ToString(System.Globalization.CultureInfo.InvariantCulture)
                ?? string.Empty,
            DeliveryAddress = conversation.DeliveryAddress
                ?? conversation.TelegramChatId?.ToString(System.Globalization.CultureInfo.InvariantCulture)
                ?? string.Empty,
            BotUsername = channel?.Username ?? conversation.BotUsername,
            RequestId = message.RequestId.Value,
            Text = message.Text,
            Attachments = message.Attachments.OrderBy(item => item.SortOrder).Select(MapDeliveryAttachment).ToList()
        };
    }

    public Task<bool> CompleteAsync(
        Guid messageId,
        Guid leaseId,
        ConversationDeliveryResultDto result,
        CancellationToken cancellationToken = default) =>
        ExecuteWithExecutionStrategyAsync(() => CompleteCoreAsync(messageId, leaseId, result, cancellationToken));

    private async Task<bool> CompleteCoreAsync(
        Guid messageId,
        Guid leaseId,
        ConversationDeliveryResultDto result,
        CancellationToken cancellationToken)
    {
        if (result.Status is not (ConversationMessageStatus.Accepted or ConversationMessageStatus.Failed
            or ConversationMessageStatus.Uncertain or ConversationMessageStatus.Cancelled))
        {
            throw new ArgumentException("Результат доставки должен завершать текущую попытку.", nameof(result));
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var conversationId = await context.OrganizationConversationMessages.AsNoTracking()
            .Where(item => item.Id == messageId)
            .Select(item => item.ConversationId)
            .SingleOrDefaultAsync(cancellationToken);
        if (conversationId == Guid.Empty)
        {
            return false;
        }

        var conversation = await context.OrganizationConversations
            .FromSqlInterpolated($"SELECT * FROM \"OrganizationConversations\" WHERE \"Id\" = {conversationId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (conversation is null)
        {
            return false;
        }

        var message = await context.OrganizationConversationMessages
            .FromSqlInterpolated($"SELECT * FROM \"OrganizationConversationMessages\" WHERE \"Id\" = {messageId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (message is null)
        {
            return false;
        }

        if (message.Status == Accepted)
        {
            return result.Status == ConversationMessageStatus.Accepted
                && message.ProviderMessageId == result.ProviderMessageId;
        }

        if (message.Status != Sending || message.LeaseId != leaseId)
        {
            return false;
        }

        message.Status = result.Status switch
        {
            ConversationMessageStatus.Accepted => Accepted,
            ConversationMessageStatus.Failed => Failed,
            ConversationMessageStatus.Uncertain => Uncertain,
            ConversationMessageStatus.Cancelled => Cancelled,
            _ => throw new ArgumentOutOfRangeException(nameof(result))
        };
        message.ProviderMessageId = result.ProviderMessageId;
        message.ErrorCode = result.ErrorCode;
        message.ErrorMessage = Limit(result.ErrorMessage, 1000);
        message.SafeToRetry = message.Status == Failed && result.SafeToRetry && message.DeliveryAttemptCount < 5;
        message.RetryAt = message.SafeToRetry && result.RetryAfterSeconds.HasValue
            ? DateTime.UtcNow.AddSeconds(Math.Clamp(result.RetryAfterSeconds.Value, 0, 86400))
            : null;
        message.DeliveredAt = message.Status == Accepted ? result.CompletedAt.ToUniversalTime() : null;
        message.LeaseId = null;
        message.LeaseExpiresAt = null;
        conversation.UpdatedAt = NextRevision(conversation.UpdatedAt);
        if (message.Status == Failed && message.SafeToRetry)
        {
            var dispatch = await context.OrganizationConversationDispatches
                .SingleOrDefaultAsync(item => item.MessageId == message.Id, cancellationToken);
            if (dispatch is not null)
            {
                dispatch.PublishedAt = null;
                dispatch.NextReconcileAt = null;
                dispatch.LeaseId = null;
                dispatch.LeaseExpiresAt = null;
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    public Task<IReadOnlyList<ConversationDispatchLeaseDto>> ClaimPendingBatchAsync(
        int batchSize,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default) =>
        ExecuteWithExecutionStrategyAsync(() => ClaimPendingBatchCoreAsync(batchSize, leaseDuration, cancellationToken));

    private async Task<IReadOnlyList<ConversationDispatchLeaseDto>> ClaimPendingBatchCoreAsync(
        int batchSize,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var take = Math.Clamp(batchSize, 1, DispatchBatchMaximum);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTime.UtcNow;
        await ExpireStaleQueuedMessagesAsync(now, take, cancellationToken);
        var recoveryCutoff = now.Subtract(s_dispatchRecoveryDelay);
        var dispatches = await context.OrganizationConversationDispatches
            .FromSqlInterpolated($"""
                SELECT * FROM "OrganizationConversationDispatches"
                WHERE ("LeaseExpiresAt" IS NULL OR "LeaseExpiresAt" <= {now})
                    AND EXISTS (
                        SELECT 1 FROM "OrganizationConversationMessages" AS message
                        WHERE message."Id" = "OrganizationConversationDispatches"."MessageId"
                            AND (
                                (message."Status" = 'queued'
                                    AND ("OrganizationConversationDispatches"."NextReconcileAt" IS NULL
                                        OR "OrganizationConversationDispatches"."NextReconcileAt" <= {now})
                                    AND ("OrganizationConversationDispatches"."PublishedAt" IS NULL
                                        OR "OrganizationConversationDispatches"."PublishedAt" <= {recoveryCutoff}))
                                OR (message."Status" = 'failed' AND message."SafeToRetry" = TRUE
                                    AND (message."RetryAt" IS NULL OR message."RetryAt" <= {now})
                                    AND "OrganizationConversationDispatches"."PublishedAt" IS NULL
                                    AND ("OrganizationConversationDispatches"."NextReconcileAt" IS NULL
                                        OR "OrganizationConversationDispatches"."NextReconcileAt" <= {now}))
                                OR (message."Status" = 'sending' AND message."LeaseExpiresAt" <= {now}
                                    AND "OrganizationConversationDispatches"."PublishedAt" <= {recoveryCutoff}
                                    AND ("OrganizationConversationDispatches"."NextReconcileAt" IS NULL
                                        OR "OrganizationConversationDispatches"."NextReconcileAt" <= {now}))
                            )
                    )
                ORDER BY "CreatedAt", "Id"
                LIMIT {take}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);
        foreach (var dispatch in dispatches)
        {
            dispatch.LeaseId = Guid.NewGuid();
            dispatch.LeaseExpiresAt = now.Add(ClampLease(leaseDuration));
            dispatch.AttemptCount++;
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        if (dispatches.Count == 0)
        {
            return [];
        }

        var requestIds = dispatches.ToDictionary(item => item.MessageId, item => item.RequestId);
        var messageIds = dispatches.Select(item => item.MessageId).ToArray();
        var providerCodes = await context.OrganizationConversationMessages.AsNoTracking()
            .Where(message => messageIds.Contains(message.Id))
            .Select(message => new
            {
                message.Id,
                ProviderCode = message.Conversation.Connection == null
                    ? Telegram
                    : message.Conversation.Connection.ProviderCode
            })
            .ToDictionaryAsync(item => item.Id, item => item.ProviderCode, cancellationToken);

        return dispatches.Select(item => new ConversationDispatchLeaseDto
        {
            DispatchId = item.Id,
            LeaseId = item.LeaseId!.Value,
            MessageId = item.MessageId,
            RequestId = requestIds[item.MessageId],
            ProviderCode = providerCodes[item.MessageId],
            IsReplay = item.PublishedAt.HasValue
        }).ToList();
    }

    private async Task ExpireStaleQueuedMessagesAsync(DateTime now, int limit, CancellationToken cancellationToken)
    {
        var cutoff = now.Subtract(s_queuedMessageMaximumAge);
        var candidates = await context.OrganizationConversationMessages.AsNoTracking()
            .Where(message => message.Direction == Outgoing
                && message.CreatedAt <= cutoff
                && (message.Status == Queued || (message.Status == Failed && message.SafeToRetry)))
            .OrderBy(message => message.ConversationId)
            .ThenBy(message => message.Sequence)
            .Select(message => new { message.Id, message.ConversationId })
            .Take(limit)
            .ToListAsync(cancellationToken);
        foreach (var conversationGroup in candidates.GroupBy(item => item.ConversationId).OrderBy(group => group.Key))
        {
            var conversation = await context.OrganizationConversations
                .FromSqlInterpolated($"SELECT * FROM \"OrganizationConversations\" WHERE \"Id\" = {conversationGroup.Key} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);
            if (conversation is null)
            {
                continue;
            }

            foreach (var candidate in conversationGroup.OrderBy(item => item.Id))
            {
                var message = await context.OrganizationConversationMessages
                    .FromSqlInterpolated($"SELECT * FROM \"OrganizationConversationMessages\" WHERE \"Id\" = {candidate.Id} FOR UPDATE")
                    .SingleOrDefaultAsync(cancellationToken);
                if (message is null || message.CreatedAt > cutoff
                    || (message.Status != Queued && !(message.Status == Failed && message.SafeToRetry)))
                {
                    continue;
                }

                message.Status = Failed;
                message.SafeToRetry = false;
                message.RetryAt = null;
                message.ErrorCode = "delivery-expired";
                message.ErrorMessage = "Сообщение не было доставлено в течение 24 часов.";
                conversation.UpdatedAt = NextRevision(conversation.UpdatedAt);
            }
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public Task<bool> CompletePublicationAsync(
        Guid dispatchId,
        Guid leaseId,
        bool brokerAccepted,
        string? errorCode = null,
        CancellationToken cancellationToken = default) =>
        ExecuteWithExecutionStrategyAsync(() => CompletePublicationCoreAsync(dispatchId, leaseId, brokerAccepted, errorCode, cancellationToken));

    private async Task<bool> CompletePublicationCoreAsync(
        Guid dispatchId,
        Guid leaseId,
        bool brokerAccepted,
        string? errorCode,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var dispatch = await context.OrganizationConversationDispatches
            .FromSqlInterpolated($"SELECT * FROM \"OrganizationConversationDispatches\" WHERE \"Id\" = {dispatchId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (dispatch is null)
        {
            return false;
        }

        if (dispatch.LeaseId != leaseId)
        {
            return false;
        }

        var now = DateTime.UtcNow;
        var wasPreviouslyPublished = dispatch.PublishedAt.HasValue;
        if (brokerAccepted)
        {
            dispatch.PublishedAt = now;
            if (wasPreviouslyPublished)
            {
                dispatch.PublicationReplayCount++;
            }
        }

        dispatch.NextReconcileAt = brokerAccepted || wasPreviouslyPublished
            ? now.Add(GetPublicationRetryDelay(dispatch.PublicationReplayCount))
            : null;
        dispatch.LeaseId = null;
        dispatch.LeaseExpiresAt = null;
        dispatch.LastErrorCode = brokerAccepted ? null : Limit(errorCode, 100);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    private static TimeSpan GetPublicationRetryDelay(int replayCount)
    {
        var delayMinutes = Math.Min(
            15 * Math.Pow(2, Math.Clamp(replayCount, 0, 3)),
            s_dispatchMaximumRetryDelay.TotalMinutes);

        return TimeSpan.FromMinutes(delayMinutes);
    }

    public async Task<ConversationDeliveryHealthDto> GetDeliveryHealthAsync(CancellationToken cancellationToken = default)
        => await BuildDeliveryHealthAsync(null, cancellationToken);

    public async Task<ConversationDeliveryHealthDto> GetDeliveryHealthForOrganizationAsync(
        string userId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        await access.EnsureViewAsync(userId, organizationId);
        await access.EnsurePermissionAsync(userId, organizationId, "messages.manage");

        return await BuildDeliveryHealthAsync(organizationId, cancellationToken);
    }

    private async Task<ConversationDeliveryHealthDto> BuildDeliveryHealthAsync(
        Guid? organizationId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var queued = context.OrganizationConversationMessages.AsNoTracking()
            .Where(message => message.Direction == Outgoing && message.Status == Queued);
        var expiredSending = context.OrganizationConversationMessages.AsNoTracking()
            .Where(message => message.Direction == Outgoing
                && message.Status == Sending
                && message.LeaseExpiresAt <= now);
        if (organizationId.HasValue)
        {
            queued = queued.Where(message => message.Conversation.OrganizationId == organizationId.Value);
            expiredSending = expiredSending.Where(message => message.Conversation.OrganizationId == organizationId.Value);
        }

        var staleQueued = queued.Where(message => message.CreatedAt <= now.Subtract(s_queuedMessageMaximumAge));

        return new ConversationDeliveryHealthDto
        {
            QueuedUnpublishedCount = await queued.CountAsync(message =>
                context.OrganizationConversationDispatches.Any(dispatch => dispatch.MessageId == message.Id && dispatch.PublishedAt == null), cancellationToken),
            QueuedPublishedCount = await queued.CountAsync(message =>
                context.OrganizationConversationDispatches.Any(dispatch => dispatch.MessageId == message.Id && dispatch.PublishedAt != null), cancellationToken),
            OldestQueuedAt = await queued.MinAsync(message => (DateTime?)message.CreatedAt, cancellationToken),
            StaleQueuedCount = await staleQueued.CountAsync(cancellationToken),
            OldestStaleQueuedAt = await staleQueued.MinAsync(message => (DateTime?)message.CreatedAt, cancellationToken),
            ExpiredSendingCount = await expiredSending.CountAsync(cancellationToken),
            OldestExpiredSendingAt = await expiredSending.MinAsync(message => (DateTime?)message.CreatedAt, cancellationToken),
            FailedCount = await context.OrganizationConversationMessages.CountAsync(message =>
                message.Direction == Outgoing && message.Status == Failed
                && (!organizationId.HasValue || message.Conversation.OrganizationId == organizationId.Value), cancellationToken),
            UncertainCount = await context.OrganizationConversationMessages.CountAsync(message =>
                message.Direction == Outgoing && message.Status == Uncertain
                && (!organizationId.HasValue || message.Conversation.OrganizationId == organizationId.Value), cancellationToken)
        };
    }

    private static ConversationDeliveryAttachmentDto MapDeliveryAttachment(OrganizationConversationAttachment attachment) => new()
    {
        Id = attachment.Id,
        Kind = attachment.Kind,
        FileName = attachment.FileName,
        ContentType = attachment.ContentType,
        SizeBytes = attachment.SizeBytes,
        ProviderFileId = attachment.ProviderFileId,
        StorageReference = attachment.StorageReference,
        SortOrder = attachment.SortOrder
    };

    private static TimeSpan ClampLease(TimeSpan leaseDuration) =>
        TimeSpan.FromSeconds(Math.Clamp(leaseDuration.TotalSeconds, 10, 300));

    private static DateTime NextRevision(DateTime previous)
    {
        var now = DateTime.UtcNow;
        return now > previous ? now : previous.AddTicks(1);
    }

    private Task<T> ExecuteWithExecutionStrategyAsync<T>(Func<Task<T>> operation) =>
        context.Database.CreateExecutionStrategy().ExecuteAsync(operation);

    private Task ExecuteWithExecutionStrategyAsync(Func<Task> operation) =>
        context.Database.CreateExecutionStrategy().ExecuteAsync(operation);

    private static string? Limit(string? value, int maximumLength) =>
        string.IsNullOrEmpty(value) ? value : value[..Math.Min(value.Length, maximumLength)];
}
