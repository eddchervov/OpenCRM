using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using Notifications.Contracts;

namespace OpenCRM.Modules.Notifications;

/// <summary> Потребитель результатов отправки уведомлений. </summary>
public sealed class NotificationResultConsumer(OpenCRMContext context, IOptions<RabbitMqOptions> options) :
    IConsumer<NotificationStatusChanged>,
    IConsumer<NotificationCommandRejected>
{
    /// <summary> Обрабатывает событие изменения статуса уведомления. </summary>
    public Task Consume(ConsumeContext<NotificationStatusChanged> consume)
    {
        var result = consume.Message;
        if (result.ApplicationId != options.Value.ApplicationId)
        {
            return Task.CompletedTask;
        }

        return this.ApplyAsync(result.IdempotencyKey, null, result, null, consume.CancellationToken);
    }

    /// <summary> Обрабатывает событие отклонения команды уведомления. </summary>
    public Task Consume(ConsumeContext<NotificationCommandRejected> consume)
    {
        var result = consume.Message;
        if (result.ApplicationId != options.Value.ApplicationId || result.MessageId == null)
        {
            return Task.CompletedTask;
        }

        return this.ApplyAsync(null, result.MessageId, null, result.ErrorCode, consume.CancellationToken);
    }

    /// <summary> Применяет результат уведомления с выбором типа канала. </summary>
    private async Task ApplyAsync(
        string? notificationKey,
        Guid? commandId,
        NotificationStatusChanged? result,
        string? rejection,
        CancellationToken cancellationToken)
    {
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            await using (transaction.ConfigureAwait(true))
            {
                if (await this.ApplyCoreAsync(notificationKey, commandId, result, rejection, cancellationToken))
                {
                    await transaction.CommitAsync(cancellationToken);
                }
            }
        });
    }

    /// <summary> Внутренняя реализация применения результата уведомления. </summary>
    private async Task<bool> ApplyCoreAsync(
        string? notificationKey,
        Guid? commandId,
        NotificationStatusChanged? result,
        string? rejection,
        CancellationToken cancellationToken)
    {
        var messages = notificationKey != null
            ? await context.EmailMessages.FromSqlInterpolated($"""
                SELECT * FROM "EmailMessages" WHERE "NotificationKey" = {notificationKey} FOR UPDATE
                """).ToListAsync(cancellationToken)
            : await context.EmailMessages.FromSqlInterpolated($"""
                SELECT * FROM "EmailMessages" WHERE "NotificationCommandId" = {commandId} FOR UPDATE
                """).ToListAsync(cancellationToken);
        var message = messages.SingleOrDefault();
        if (message == null)
        {
            await this.ApplyTelegramAsync(notificationKey, commandId, result, rejection, cancellationToken);
            await this.ApplyMaxAsync(notificationKey, commandId, result, rejection, cancellationToken);
            await this.ApplyTelegramClientAsync(notificationKey, commandId, result, rejection, cancellationToken);

            return true;
        }

        if (result?.Content != null)
        {
            message.Subject = result.Content.Subject ?? message.Subject;
            message.HtmlBody = result.Content.IsHtml ? result.Content.Body : string.Empty;
            message.TextBody = result.Content.TextBody;
        }

        if (IsTerminal(message.NotificationStatus))
        {
            await context.SaveChangesAsync(cancellationToken);

            return true;
        }

        if (result != null)
        {
            if (result.OccurredAt.UtcDateTime <= message.NotificationEventAt
                || result.AttemptCount < message.NotificationAttemptCount)
            {
                return false;
            }

            if (result.Status == "not-found")
            {
                // Повторяем исходную команду с тем же ключом, если Worker её ещё не сохранил.
                message.NotificationDispatchedAt = null;
                message.NextDispatchAt = DateTime.UtcNow;
            }
            else
            {
                if (result.Status is not ("queued" or "processing" or "retrying" or "provider-accepted" or "simulated" or "failed"))
                {
                    throw new InvalidOperationException($"Неизвестный статус уведомления: {result.Status}.");
                }

                message.NotificationStatus = result.Status;
                message.NotificationId = result.NotificationId;
                message.NotificationAttemptCount = result.AttemptCount;
                message.NotificationEventAt = result.OccurredAt.UtcDateTime;
                message.ErrorMessage = result.ErrorCode;
                message.ProviderMessageId = result.ProviderMessageId ?? message.ProviderMessageId;
                ApplyDisplayStatus(message, result.Status);
            }
        }
        else
        {
            message.NotificationStatus = "failed";
            message.ErrorMessage = rejection;
            ApplyDisplayStatus(message, "failed");
        }

        if (IsTerminal(message.NotificationStatus))
        {
            message.NextDispatchAt = null;
            await this.CompleteRelatedRecordsAsync(message, cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);

        return true;
    }

    /// <summary> Применяет результат отправки Telegram-уведомления. </summary>
    private async Task ApplyTelegramAsync(
        string? notificationKey,
        Guid? commandId,
        NotificationStatusChanged? result,
        string? rejection,
        CancellationToken cancellationToken)
    {
        var messages = notificationKey != null
            ? await context.TelegramNotificationOutbox.FromSqlInterpolated($"""
                SELECT * FROM "TelegramNotificationOutbox" WHERE "NotificationKey" = {notificationKey} FOR UPDATE
                """).ToListAsync(cancellationToken)
            : await context.TelegramNotificationOutbox.FromSqlInterpolated($"""
                SELECT * FROM "TelegramNotificationOutbox" WHERE "NotificationCommandId" = {commandId} FOR UPDATE
                """).ToListAsync(cancellationToken);
        var message = messages.SingleOrDefault();
        if (message == null || IsTerminal(message.NotificationStatus))
        {
            return;
        }

        if (result != null)
        {
            if (result.OccurredAt.UtcDateTime <= message.NotificationEventAt
                || result.AttemptCount < message.AttemptCount)
            {
                return;
            }

            if (result.Status == "not-found")
            {
                message.NotificationDispatchedAt = null;
                message.NextDispatchAt = DateTime.UtcNow;
            }
            else
            {
                if (result.Status is not ("queued" or "processing" or "retrying" or "provider-accepted" or "simulated" or "failed"))
                {
                    throw new InvalidOperationException($"Неизвестный статус уведомления: {result.Status}.");
                }

                message.NotificationStatus = result.Status;
                message.NotificationId = result.NotificationId;
                message.ProviderMessageId = result.ProviderMessageId ?? message.ProviderMessageId;
                message.ErrorCode = result.ErrorCode;
                message.AttemptCount = result.AttemptCount;
                message.NotificationEventAt = result.OccurredAt.UtcDateTime;
            }
        }
        else
        {
            message.NotificationStatus = "failed";
            message.ErrorCode = rejection;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary> Применяет результат отправки уведомления Max. </summary>
    private async Task ApplyMaxAsync(
        string? notificationKey,
        Guid? commandId,
        NotificationStatusChanged? result,
        string? rejection,
        CancellationToken cancellationToken)
    {
        var messages = notificationKey != null
            ? await context.MaxClientNotifications.FromSqlInterpolated($"""
                SELECT * FROM "MaxClientNotifications" WHERE "NotificationKey" = {notificationKey} FOR UPDATE
                """).ToListAsync(cancellationToken)
            : await context.MaxClientNotifications.FromSqlInterpolated($"""
                SELECT * FROM "MaxClientNotifications" WHERE "NotificationCommandId" = {commandId} FOR UPDATE
                """).ToListAsync(cancellationToken);
        var message = messages.SingleOrDefault();
        if (message == null || message.Status is "sent" or "simulated" or "failed" or "skipped")
        {
            return;
        }

        if (result == null)
        {
            message.Status = "failed";
            message.ErrorCode = rejection;
            await context.SaveChangesAsync(cancellationToken);

            return;
        }

        if (result.OccurredAt.UtcDateTime <= message.NotificationEventAt
            || result.AttemptCount < message.AttemptCount)
        {
            return;
        }

        if (result.Status == "not-found")
        {
            message.Status = "queued";
            message.NotificationDispatchedAt = null;
            message.NextAttemptAt = DateTime.UtcNow;
        }
        else
        {
            message.Status = NotificationResultConsumer.ResolveClientStatus(result.Status);
            message.NotificationId = result.NotificationId;
            message.ProviderMessageId = result.ProviderMessageId ?? message.ProviderMessageId;
            message.ErrorCode = result.ErrorCode;
            message.AttemptCount = result.AttemptCount;
            message.NotificationEventAt = result.OccurredAt.UtcDateTime;
            if (message.Status == "sent")
            {
                message.SentAt = result.OccurredAt.UtcDateTime;
            }

            message.NextAttemptAt = DateTime.UtcNow.AddSeconds(options.Value.ResponseTimeoutSeconds);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary> Применяет результат отправки клиентского Telegram-уведомления. </summary>
    private async Task ApplyTelegramClientAsync(
        string? notificationKey,
        Guid? commandId,
        NotificationStatusChanged? result,
        string? rejection,
        CancellationToken cancellationToken)
    {
        var messages = notificationKey != null
            ? await context.TelegramClientNotifications.FromSqlInterpolated($"""
                SELECT * FROM "TelegramClientNotifications" WHERE "NotificationKey" = {notificationKey} FOR UPDATE
                """).ToListAsync(cancellationToken)
            : await context.TelegramClientNotifications.FromSqlInterpolated($"""
                SELECT * FROM "TelegramClientNotifications" WHERE "NotificationCommandId" = {commandId} FOR UPDATE
                """).ToListAsync(cancellationToken);
        var message = messages.SingleOrDefault();
        if (message == null || message.Status is "sent" or "simulated" or "failed" or "skipped")
        {
            return;
        }

        if (result == null)
        {
            message.Status = "failed";
            message.ErrorCode = rejection;
            await context.SaveChangesAsync(cancellationToken);

            return;
        }

        if (result.OccurredAt.UtcDateTime <= message.NotificationEventAt
            || result.AttemptCount < message.AttemptCount)
        {
            return;
        }

        if (result.Status == "not-found")
        {
            message.Status = "queued";
            message.NotificationDispatchedAt = null;
            message.NextAttemptAt = DateTime.UtcNow;
        }
        else
        {
            message.Status = NotificationResultConsumer.ResolveClientStatus(result.Status);
            message.NotificationId = result.NotificationId;
            message.ProviderMessageId = result.ProviderMessageId ?? message.ProviderMessageId;
            message.ErrorCode = result.ErrorCode;
            message.AttemptCount = result.AttemptCount;
            message.NotificationEventAt = result.OccurredAt.UtcDateTime;
            if (message.Status == "sent")
            {
                message.SentAt = result.OccurredAt.UtcDateTime;
            }

            message.NextAttemptAt = DateTime.UtcNow.AddSeconds(options.Value.ResponseTimeoutSeconds);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary> Завершает связанные записи email-сообщения по итоговому статусу. </summary>
    private async Task CompleteRelatedRecordsAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var accepted = message.NotificationStatus == "provider-accepted";
        var simulated = message.NotificationStatus == "simulated";
        if (message.SubscriptionReminderId != null)
        {
            var reminder = await context.SubscriptionExpiryReminders
                .FirstOrDefaultAsync(x => x.Id == message.SubscriptionReminderId, cancellationToken);
            if (reminder != null)
            {
                reminder.SentAt = accepted ? message.SentAt : null;
                reminder.ErrorMessage = simulated ? "simulated" : message.ErrorMessage;
            }
        }

        if (message.CampaignRecipientId == null)
        {
            return;
        }

        var campaignId = await context.EmailCampaignRecipients
            .Where(x => x.Id == message.CampaignRecipientId)
            .Select(x => (Guid?)x.EmailCampaignId)
            .SingleOrDefaultAsync(cancellationToken);
        if (campaignId == null)
        {
            return;
        }

        var campaigns = await context.EmailCampaigns.FromSqlInterpolated($"""
            SELECT * FROM "EmailCampaigns" WHERE "Id" = {campaignId} FOR UPDATE
            """).ToListAsync(cancellationToken);
        var campaign = campaigns.Single();
        var recipient = await context.EmailCampaignRecipients.SingleAsync(x => x.Id == message.CampaignRecipientId, cancellationToken);
        if (recipient.StatusCode is EmailCampaignRecipientStatusCodes.Sent
            or EmailCampaignRecipientStatusCodes.Failed or EmailCampaignRecipientStatusCodes.Simulated)
        {
            return;
        }

        recipient.StatusCode = accepted ? EmailCampaignRecipientStatusCodes.Sent
            : simulated ? EmailCampaignRecipientStatusCodes.Simulated : EmailCampaignRecipientStatusCodes.Failed;
        recipient.SentAt = accepted ? message.SentAt : null;
        recipient.ErrorMessage = message.ErrorMessage;
        if (accepted)
        {
            campaign.SentCount++;
        }
        else if (simulated)
        {
            campaign.SimulatedCount++;
        }
        else
        {
            campaign.FailedCount++;
            campaign.LastError = message.ErrorMessage;
        }

        campaign.UpdatedAt = DateTime.UtcNow;
        if (campaign.SentCount + campaign.SimulatedCount + campaign.FailedCount + campaign.SkippedCount >= campaign.TotalCount
            && campaign.StatusCode != EmailCampaignStatusCodes.Canceled)
        {
            campaign.StatusCode = EmailCampaignStatusCodes.Completed;
            campaign.CompletedAt = DateTime.UtcNow;
        }
    }

    /// <summary> Определяет, является ли статус итоговым. </summary>
    private static bool IsTerminal(string? status)
    {
        return status is "provider-accepted" or "simulated" or "failed";
    }

    /// <summary> Преобразует внутренний статус в статус для отображения клиенту. </summary>
    private static string ResolveClientStatus(string status)
    {
        return status switch
        {
            "queued" or "processing" => "processing",
            "retrying" => "retrying",
            "provider-accepted" => "sent",
            "simulated" => "simulated",
            "failed" => "failed",
            _ => throw new InvalidOperationException($"Неизвестный статус уведомления: {status}.")
        };
    }

    /// <summary> Устанавливает отображаемый статус email-сообщения. </summary>
    private static void ApplyDisplayStatus(EmailMessage message, string status)
    {
        if (status == "provider-accepted")
        {
            message.SentAt ??= message.NotificationEventAt;
        }

        // События доставки, открытия и жалоб от провайдера точнее состояния Worker.
        if (message.StatusCode is not (EmailMessageStatusCodes.Pending or EmailMessageStatusCodes.Queued
            or EmailMessageStatusCodes.Retrying or EmailMessageStatusCodes.Accepted))
        {
            return;
        }

        message.StatusCode = status switch
        {
            "provider-accepted" => EmailMessageStatusCodes.Sent,
            "simulated" => EmailMessageStatusCodes.Simulated,
            "failed" => EmailMessageStatusCodes.Failed,
            "retrying" => EmailMessageStatusCodes.Retrying,
            _ => EmailMessageStatusCodes.Queued
        };
    }

}
