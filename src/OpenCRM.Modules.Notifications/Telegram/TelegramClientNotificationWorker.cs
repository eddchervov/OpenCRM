using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;
using Notifications.Contracts;

namespace OpenCRM.Modules.Notifications.Telegram;

/// <summary> Фоновый сервис отправки клиентских Telegram-уведомлений. </summary>
public sealed class TelegramClientNotificationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> rabbitMqOptions,
    ILogger<TelegramClientNotificationWorker> logger) : BackgroundService
{
    /// <summary> Цикл фоновой обработки клиентских Telegram-уведомлений. </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (true)
        {
            stoppingToken.ThrowIfCancellationRequested();

            try
            {
                await this.ProcessNextAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Ошибка обработки очереди клиентских уведомлений Telegram.");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    /// <summary> Обрабатывает следующее клиентское Telegram-уведомление. </summary>
    private async Task ProcessNextAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OpenCRMContext>();
        var item = await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            TelegramClientNotification? claimed;
            await using (transaction.ConfigureAwait(true))
            {
                claimed = await this.ClaimNextAsync(context, cancellationToken);
                if (claimed != null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
            }

            return claimed;
        });

        if (item == null)
        {
            return;
        }

        var bot = await context.OrganizationTelegramBots.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrganizationId == item.OrganizationId, cancellationToken);
        var client = await context.OrganizationClients.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == item.ClientId && x.OrganizationId == item.OrganizationId, cancellationToken);
        var maintenance = await context.Maintenance.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == item.MaintenanceId, cancellationToken);
        var organization = await context.Organizations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == item.OrganizationId, cancellationToken);
        if (item.NotificationDispatchedAt == null
            && (organization?.OwnerUserId == null
                || !await scope.ServiceProvider.GetRequiredService<OpenCRM.Services.Interfaces.Organizations.IOrganizationSubscriptionService>()
                    .HasFeatureAsync(organization.OwnerUserId, item.OrganizationId, Helpers.FeatureCodes.ClientMessaging)))
        {
            item.Status = "skipped";
            item.ErrorCode = "client-messaging-pro-required";
            await context.SaveChangesAsync(cancellationToken);
            return;
        }

        var vehicle = maintenance == null ? null : await context.Vehicles.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == maintenance.VehicleId, cancellationToken);
        var hasNewerNotification = await context.TelegramClientNotifications.AsNoTracking()
            .AnyAsync(x => x.MaintenanceId == item.MaintenanceId && x.ClientId == item.ClientId
                && x.Id > item.Id && x.EventCode != MaintenanceVisitReminderRules.EventCode, cancellationToken);
        var isReminderCurrent = item.EventCode != MaintenanceVisitReminderRules.EventCode
            || await VisitReminderDeliveryGuard.IsCurrentAsync(context, scope.ServiceProvider.GetRequiredService<IConfiguration>(),
                maintenance, item.OrganizationId, client, item.VisitStartsAtUtc, item.NotificationKey, "telegram", cancellationToken);

        if (item.NotificationDispatchedAt == null && (bot?.IsEnabled != true || client?.TelegramNotificationsEnabled != true
            || client.IsDeleted || client.TelegramChatId != item.ChatId
            || client.TelegramConnectedAt == null || client.TelegramConnectedAt.Value > item.CreatedAt
            || maintenance == null
            || organization == null || organization.IsDeleted || organization.IsArchived || !organization.IsActive
            || vehicle?.OrganizationId != item.OrganizationId || vehicle.OrganizationClientId != item.ClientId
            || hasNewerNotification || !isReminderCurrent
            || item.EventCode == "ready" && !bot.NotifyReady
            || item.EventCode != "ready" && !bot.NotifyBooking
            || item.EventCode == "ready" && (!maintenance.ReadyForHandoverAt.HasValue || maintenance.HandedOverAt.HasValue)
            || item.EventCode == "booking_cancelled" && maintenance.Status != MaintenanceStatus.Cancelled
            || item.EventCode is "booking_created" or "booking_changed"
                && maintenance.Status is not (MaintenanceStatus.Planned or MaintenanceStatus.InProgress)))
        {
            item.Status = "skipped";
            item.ErrorCode = "state-changed";
            await context.SaveChangesAsync(cancellationToken);
            return;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(rabbitMqOptions.Value.SendTimeoutSeconds));
        var endpoints = scope.ServiceProvider.GetRequiredService<ISendEndpointProvider>();
        var endpoint = await endpoints.GetSendEndpoint(
            new Uri($"queue:{NotificationQueues.Commands(rabbitMqOptions.Value.ApplicationId)}"));
        var commandId = Guid.NewGuid();
        if (item.NotificationDispatchedAt == null)
        {
            var request = new CreateNotificationRequest(
                item.NotificationKey,
                NotificationChannels.TelegramClient,
                $"{item.OrganizationId:N}:{item.ChatId}",
                null,
                null,
                null,
                item.Body,
                null);
            await endpoint.Send(new SendNotification(request),
                send => send.MessageId = commandId, timeout.Token);
            item.NotificationDispatchedAt = DateTime.UtcNow;
        }
        else
        {
            await endpoint.Send(new GetNotificationStatus(item.NotificationKey),
                send => send.MessageId = commandId, timeout.Token);
        }

        item.NotificationCommandId = commandId;
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary> Атомарно занимает следующее клиентское Telegram-уведомление. </summary>
    private async Task<TelegramClientNotification?> ClaimNextAsync(
        OpenCRMContext context,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var rows = await context.TelegramClientNotifications.FromSqlInterpolated($"""
            SELECT * FROM "TelegramClientNotifications"
            WHERE "Status" IN ('queued', 'retrying', 'processing') AND "NextAttemptAt" <= {now}
            ORDER BY "NextAttemptAt", "Id" LIMIT 1 FOR UPDATE SKIP LOCKED
            """).ToListAsync(cancellationToken);
        var item = rows.SingleOrDefault();
        if (item == null)
        {
            return null;
        }

        item.Status = "processing";
        item.NextAttemptAt = now.AddSeconds(rabbitMqOptions.Value.ResponseTimeoutSeconds);
        await context.SaveChangesAsync(cancellationToken);

        return item;
    }
}
