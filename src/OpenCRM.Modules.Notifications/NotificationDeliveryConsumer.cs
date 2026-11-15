using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using Notifications.Contracts;

namespace OpenCRM.Modules.Notifications;

/// <summary>Сохраняет нормализованные события МС без знания формата или ключей почтового провайдера.</summary>
public sealed class NotificationDeliveryConsumer(OpenCRMContext context, IOptions<RabbitMqOptions> options)
    : IConsumer<NotificationDeliveryReported>
{
    /// <summary> Применяет отчёт о доставке уведомления к сообщению. </summary>
    public async Task Consume(ConsumeContext<NotificationDeliveryReported> consume)
    {
        var result = consume.Message;
        if (result.ApplicationId != options.Value.ApplicationId || string.IsNullOrWhiteSpace(result.IdempotencyKey))
        {
            return;
        }

        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            var cancellationToken = consume.CancellationToken;
            var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            bool applied;
            await using (transaction.ConfigureAwait(true))
            {
                applied = await this.ApplyDeliveryCoreAsync(result, cancellationToken);
                if (applied)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
            }

            return applied;
        });
    }

    /// <summary> Внутренняя реализация применения отчёта о доставке. </summary>
    private async Task<bool> ApplyDeliveryCoreAsync(
        NotificationDeliveryReported result,
        CancellationToken cancellationToken)
    {
        var lockKey = BitConverter.ToInt64(result.EventId.ToByteArray());
        await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);
        if (await context.EmailMessageEvents.AnyAsync(x => x.NotificationEventId == result.EventId, cancellationToken))
        {
            return false;
        }

        var messages = await context.EmailMessages.FromSqlInterpolated($"""
            SELECT * FROM "EmailMessages" WHERE "NotificationKey" = {result.IdempotencyKey} FOR UPDATE
            """).ToListAsync(cancellationToken);
        var message = messages.SingleOrDefault();
        if (message == null)
        {
            return false;
        }

        context.EmailMessageEvents.Add(new EmailMessageEvent
        {
            NotificationEventId = result.EventId,
            EmailMessageId = message.Id,
            Provider = EmailProviders.NotificationService,
            EventName = result.Status,
            Status = result.Status,
            Email = result.Recipient,
            ProviderMessageId = result.ProviderMessageId,
            Url = Truncate(result.Url, 2000),
            IpAddress = Truncate(result.IpAddress, 100),
            UserAgent = Truncate(result.UserAgent, 1000),
            DeliveryInfo = result.Details,
            RawPayload = JsonSerializer.Serialize(result),
            SignatureVerified = true,
            EventAt = result.OccurredAt.UtcDateTime,
            ReceivedAt = DateTime.UtcNow
        });

        ApplyToMessage(message, result);

        if (result.Status is EmailMessageStatusCodes.Unsubscribed or EmailMessageStatusCodes.Spam
            && !string.IsNullOrWhiteSpace(result.Recipient))
        {
            var email = result.Recipient.Trim().ToUpperInvariant();
            await context.ApplicationUsers.Where(x => x.NormalizedEmail == email && x.AdvertisingConsentAccepted)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(x => x.AdvertisingConsentAccepted, false)
                    .SetProperty(x => x.AdvertisingConsentAcceptedAt, (DateTime?)null), cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);

        return true;
    }

    /// <summary> Обновляет поля email-сообщения по результату доставки. </summary>
    private static void ApplyToMessage(EmailMessage message, NotificationDeliveryReported result)
    {
        message.Provider = EmailProviders.NotificationService;
        message.ProviderMessageId ??= result.ProviderMessageId;
        if (result.Status == EmailMessageStatusCodes.Opened)
        {
            message.OpenCount++;
        }
        else if (result.Status == EmailMessageStatusCodes.Clicked)
        {
            message.ClickCount++;
        }

        if (message.LastEventAt > result.OccurredAt.UtcDateTime)
        {
            return;
        }

        message.LastEventAt = result.OccurredAt.UtcDateTime;
        message.LastEventCode = result.Status;
        message.StatusCode = result.Status;
    }

    /// <summary> Усекает текст до заданной длины. </summary>
    private static string? Truncate(string? value, int length)
    {
        return value?.Length > length ? value[..length] : value;
    }
}
