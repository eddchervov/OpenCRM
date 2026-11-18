using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Maintenance.Reminders;

/// <summary> Настраивает и выпускает напоминания, используя существующие очереди каналов. </summary>
internal sealed class MaintenanceVisitReminderService(
    OpenCRMContext context,
    ISystemClock clock,
    IConfiguration configuration,
    IOrganizationSubscriptionService subscriptions,
    IOrganizationPermissionService permissions,
    MaintenanceAccess access,
    MaintenanceOrderLoader loader,
    MaintenanceHistoryWriter history,
    MaintenanceTransaction transaction) : IMaintenanceVisitReminderService
{
    public async Task<VisitReminderSettingsDto> GetSettingsAsync(string userId, Guid organizationId)
    {
        await subscriptions.EnsureWorkspaceCanReadAsync(userId, organizationId, OrganizationPermissionCodes.WorkspaceProfileEdit);
        var organization = await context.Organizations.AsNoTracking().SingleAsync(item => item.Id == organizationId);

        return new VisitReminderSettingsDto
        {
            HoursBefore = organization.VisitReminderHoursBefore,
            ChannelCode = organization.VisitReminderChannelCode,
            Template = organization.VisitReminderTemplate ?? MaintenanceVisitReminderRules.DefaultTemplate,
            TimeZoneId = organization.ShiftTimeZoneId
        };
    }

    public async Task SaveSettingsAsync(string userId, Guid organizationId, VisitReminderSettingsDto settings)
    {
        if (settings.HoursBefore.HasValue
            && !await subscriptions.HasFeatureAsync(userId, organizationId, Helpers.FeatureCodes.ClientMessaging))
        {
            throw new InvalidOperationException("Автоматические напоминания клиентам доступны по подписке PRO.");
        }

        if (settings.HoursBefore is not (null or 2 or 24 or 48)
            || settings.ChannelCode is not ("auto" or "telegram" or "max" or "both"))
        {
            throw new ArgumentException("Выберите срок напоминания и доступный канал отправки.");
        }

        VisitReminderContent.Validate(settings.Template);

        await transaction.ExecuteAsync(async () =>
        {
            await subscriptions.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.WorkspaceProfileEdit);
            await transaction.LockOrganizationAsync(organizationId);
            var organization = await context.Organizations.SingleAsync(item => item.Id == organizationId);
            if (settings.HoursBefore.HasValue && (string.IsNullOrWhiteSpace(organization.ShiftTimeZoneId)
                || !TimeZoneInfo.TryFindSystemTimeZoneById(organization.ShiftTimeZoneId, out _)))
            {
                throw new ArgumentException("Сначала укажите часовой пояс организации в настройках графика работы.");
            }

            organization.VisitReminderHoursBefore = settings.HoursBefore;
            organization.VisitReminderChannelCode = settings.ChannelCode;
            organization.VisitReminderTemplate = settings.Template.Trim();
            context.OrganizationAuditEvents.Add(new OrganizationAuditEvent
            {
                OrganizationId = organizationId,
                ActorUserId = userId,
                EventType = "visit_reminder_settings_changed",
                Title = "Изменены настройки напоминаний о визите",
                Description = settings.HoursBefore.HasValue ? $"За {settings.HoursBefore} ч., канал: {settings.ChannelCode}." : "Напоминания отключены.",
                CreatedAt = clock.UtcNow
            });
            await context.SaveChangesAsync();
        }, () => context.Organizations.AsNoTracking().AnyAsync(item => item.Id == organizationId
            && item.VisitReminderHoursBefore == settings.HoursBefore
            && item.VisitReminderChannelCode == settings.ChannelCode && item.VisitReminderTemplate == settings.Template.Trim()));
    }

    public async Task<MaintenanceVisitReminderDto> GetAsync(string userId, Guid organizationId, Guid maintenanceId)
    {
        await access.EnsureCanReadWorkspaceAsync(userId, organizationId);
        var maintenance = await this.LoadAsync(organizationId, maintenanceId);
        var organization = await context.Organizations.AsNoTracking().SingleAsync(item => item.Id == organizationId);
        var startsAt = MaintenanceVisitReminderRules.GetStartsAtUtc(maintenance, organization.ShiftTimeZoneId);
        var scheduledAt = startsAt.HasValue && organization.VisitReminderHoursBefore.HasValue
            ? startsAt.Value.AddHours(-organization.VisitReminderHoursBefore.Value) : (DateTime?)null;
        var channels = await this.GetChannelsAsync(organization, maintenance.Vehicle.OrganizationClient);
        var status = !maintenance.VisitReminderEnabled || !organization.VisitReminderHoursBefore.HasValue ? "disabled"
            : maintenance.Status != MaintenanceStatus.Planned ? "not_planned"
            : startsAt == null ? "time_zone_required"
            : startsAt <= clock.UtcNow ? "past"
            : channels.Count == 0 ? "no_channel" : "scheduled";

        if (status == "scheduled")
        {
            var keys = channels.Select(channel => MaintenanceVisitReminderRules.GetNotificationKey(maintenanceId,
                startsAt!.Value, organization.VisitReminderHoursBefore!.Value, channel)).ToArray();

            var statuses = await context.TelegramClientNotifications.AsNoTracking()
                .Where(item => keys.Contains(item.NotificationKey)
                    || channels.Contains("telegram") && item.MaintenanceId == maintenanceId
                        && item.VisitStartsAtUtc == startsAt && item.EventCode == MaintenanceVisitReminderRules.EventCode && item.Status == "sent")
                .Select(item => item.Status)
                .Concat(context.MaxClientNotifications.AsNoTracking()
                    .Where(item => keys.Contains(item.NotificationKey)
                        || channels.Contains("max") && item.MaintenanceId == maintenanceId
                            && item.VisitStartsAtUtc == startsAt && item.EventCode == MaintenanceVisitReminderRules.EventCode && item.Status == "sent")
                    .Select(item => item.Status)).ToListAsync();
            status = statuses.Contains("failed") ? "failed" : statuses.Contains("retrying") ? "retrying"
                : statuses.Contains("processing") ? "processing" : statuses.Contains("queued") ? "queued"
                : statuses.Contains("sent") ? "sent" : statuses.Contains("simulated") ? "simulated"
                : statuses.Contains("skipped") ? "skipped" : status;
        }

        var statusDetailCode = status == "disabled"
            ? organization.VisitReminderHoursBefore.HasValue ? "disabled_record" : "disabled_organization"
            : status;

        var text = status switch
        {
            "disabled" => organization.VisitReminderHoursBefore.HasValue
                ? "Напоминание отключено для этой записи." : "Напоминания выключены в настройках организации.",
            "not_planned" => "Напоминание отправляется только для плановой записи.",
            "time_zone_required" => "Проверьте дату, время визита и часовой пояс организации; неоднозначное время не используется.",
            "past" => "Время визита уже наступило; напоминание не отправляется.",
            "no_channel" => "Нет доступного канала: проверьте подключение клиента, бота и настройки отправки.",
            "sent" => "Провайдер принял напоминание; прочтение клиентом не подтверждено.",
            "simulated" => "Тестовая отправка; клиенту сообщение не отправлялось.",
            "failed" => "Не удалось отправить напоминание. Подробности — в истории уведомлений.",
            "skipped" => "Отправка пропущена после изменения состояния записи или канала.",
            "scheduled" => $"Запланировано: {TimeZoneInfo.ConvertTimeFromUtc(scheduledAt!.Value, TimeZoneInfo.FindSystemTimeZoneById(organization.ShiftTimeZoneId!)):dd.MM.yyyy HH:mm} по времени организации.",
            _ => "Напоминание в очереди отправки."
        };

        var workspaceAccess = await subscriptions.GetWorkspaceAccessAsync(userId, organizationId);
        var canPreview = await permissions.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.ClientsView)
            && await permissions.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.VehiclesView);

        return new MaintenanceVisitReminderDto(maintenance.VisitReminderEnabled, maintenance.Version, startsAt, scheduledAt,
            status, text, organization.VisitReminderChannelCode,
            canPreview ? VisitReminderContent.Build(maintenance, organization, maintenance.Vehicle.OrganizationClient?.Name) : string.Empty,
            workspaceAccess?.IsReadOnly == false
                && await permissions.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.MaintenanceEdit),
            organization.ShiftTimeZoneId,
            statusDetailCode);
    }

    public async Task<MaintenanceVisitReminderDto> SetEnabledAsync(string userId, Guid organizationId, Guid maintenanceId, bool enabled, uint version)
    {
        if (enabled && !await subscriptions.HasFeatureAsync(userId, organizationId, Helpers.FeatureCodes.ClientMessaging))
        {
            throw new InvalidOperationException("Напоминания клиентам доступны по подписке PRO.");
        }

        uint? savedVersion = null;
        await transaction.ExecuteAsync(async () =>
        {
            await access.EnsureCanWriteWorkspaceAsync(userId, organizationId);
            var maintenance = await loader.LockMaintenanceAsync(userId, organizationId, maintenanceId);
            if (maintenance.Version != version)
            {
                throw new DbUpdateConcurrencyException("Запись изменилась. Обновите карточку и повторите действие.");
            }

            if (maintenance.VisitReminderEnabled != enabled)
            {
                maintenance.VisitReminderEnabled = enabled;
                await history.AddHistoryEventAsync(userId, organizationId, maintenance.VehicleId, maintenanceId,
                    "visit_reminder_changed", enabled ? "Напоминание о визите включено" : "Напоминание о визите отключено", string.Empty);
                await context.SaveChangesAsync();
            }

            savedVersion = maintenance.Version;
        }, () => context.Maintenance.AsNoTracking().AnyAsync(item => item.Id == maintenanceId && item.Version == savedVersion));

        return await this.GetAsync(userId, organizationId, maintenanceId);
    }

    public async Task SendDueAsync(CancellationToken cancellationToken)
    {
        var organizations = await context.Organizations.AsNoTracking()
            .Where(item => item.VisitReminderHoursBefore != null && item.IsActive && !item.IsDeleted && !item.IsArchived)
            .ToListAsync(cancellationToken);
        foreach (var organization in organizations)
        {
            if (string.IsNullOrWhiteSpace(organization.ShiftTimeZoneId)
                || !TimeZoneInfo.TryFindSystemTimeZoneById(organization.ShiftTimeZoneId, out var zone))
            {
                continue;
            }

            var from = DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(clock.UtcNow, zone).Date, DateTimeKind.Utc);
            var to = DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(clock.UtcNow.AddHours(organization.VisitReminderHoursBefore!.Value), zone).Date.AddDays(1), DateTimeKind.Utc);
            var offset = 0;
            while (true)
            {
                var candidates = await context.Maintenance.AsNoTracking()
                    .Where(item => !item.IsDeleted && item.Status == MaintenanceStatus.Planned && item.VisitReminderEnabled
                        && !item.Vehicle.IsDeleted && item.Vehicle.OrganizationId == organization.Id
                        && item.PlannedDate >= from && item.PlannedDate < to && item.PlannedStartTime != null)
                    .OrderBy(item => item.PlannedDate).ThenBy(item => item.PlannedStartTime).ThenBy(item => item.Id)
                    .Skip(offset).Take(100).Select(item => new MaintenanceEntity
                    {
                        Id = item.Id, PlannedDate = item.PlannedDate, PlannedStartTime = item.PlannedStartTime, Status = item.Status
                    }).ToListAsync(cancellationToken);
                var ids = candidates.Select(item => item.Id).ToArray();
                var existing = await context.TelegramClientNotifications.AsNoTracking()
                    .Where(item => ids.Contains(item.MaintenanceId) && item.EventCode == MaintenanceVisitReminderRules.EventCode)
                    .Select(item => item.NotificationKey)
                    .Concat(context.MaxClientNotifications.AsNoTracking()
                        .Where(item => ids.Contains(item.MaintenanceId) && item.EventCode == MaintenanceVisitReminderRules.EventCode)
                        .Select(item => item.NotificationKey)).ToHashSetAsync(cancellationToken);

                foreach (var candidate in candidates)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var startsAt = MaintenanceVisitReminderRules.GetStartsAtUtc(candidate, organization.ShiftTimeZoneId);
                    if (!MaintenanceVisitReminderRules.IsCurrent(candidate, organization, startsAt, clock.UtcNow))
                    {
                        continue;
                    }

                    var telegramKey = MaintenanceVisitReminderRules.GetNotificationKey(candidate.Id, startsAt!.Value, organization.VisitReminderHoursBefore.Value, "telegram");
                    var maxKey = MaintenanceVisitReminderRules.GetNotificationKey(candidate.Id, startsAt.Value, organization.VisitReminderHoursBefore.Value, "max");
                    var isQueued = organization.VisitReminderChannelCode switch
                    {
                        "both" => existing.Contains(telegramKey) && existing.Contains(maxKey),
                        "telegram" => existing.Contains(telegramKey),
                        "max" => existing.Contains(maxKey),
                        _ => existing.Contains(telegramKey) || existing.Contains(maxKey)
                    };
                    if (isQueued)
                    {
                        continue;
                    }

                    await this.QueueDueAsync(organization.Id, candidate.Id);
                    context.ChangeTracker.Clear();
                }

                if (candidates.Count < 100)
                {
                    break;
                }

                offset += candidates.Count;
            }
        }
    }

    private Task QueueDueAsync(Guid organizationId, Guid maintenanceId)
    {
        return transaction.ExecuteAsync(async () =>
        {
            await transaction.LockOrganizationAsync(organizationId);
            var organization = await context.Organizations.AsNoTracking().SingleAsync(item => item.Id == organizationId);
            var maintenance = await this.LoadAsync(organizationId, maintenanceId);
            var startsAt = MaintenanceVisitReminderRules.GetStartsAtUtc(maintenance, organization.ShiftTimeZoneId);
            if (!MaintenanceVisitReminderRules.IsCurrent(maintenance, organization, startsAt, clock.UtcNow)
                || startsAt!.Value.AddHours(-organization.VisitReminderHoursBefore!.Value) > clock.UtcNow)
            {
                return;
            }

            var client = maintenance.Vehicle.OrganizationClient;
            var channels = await this.GetChannelsAsync(organization, client);
            var sentTelegram = await context.TelegramClientNotifications.AsNoTracking().AnyAsync(item =>
                item.MaintenanceId == maintenanceId && item.EventCode == MaintenanceVisitReminderRules.EventCode
                && item.VisitStartsAtUtc == startsAt && item.Status == "sent");
            var sentMax = await context.MaxClientNotifications.AsNoTracking().AnyAsync(item =>
                item.MaintenanceId == maintenanceId && item.EventCode == MaintenanceVisitReminderRules.EventCode
                && item.VisitStartsAtUtc == startsAt && item.Status == "sent");
            if (organization.VisitReminderChannelCode == "auto" && (sentTelegram || sentMax))
            {
                return;
            }

            var body = VisitReminderContent.Build(maintenance, organization, client?.Name);
            foreach (var channel in channels)
            {
                var key = MaintenanceVisitReminderRules.GetNotificationKey(maintenanceId, startsAt.Value,
                    organization.VisitReminderHoursBefore!.Value, channel);
                if (channel == "telegram" && !sentTelegram && !await context.TelegramClientNotifications.AnyAsync(item => item.NotificationKey == key))
                {
                    context.TelegramClientNotifications.Add(new TelegramClientNotification
                    {
                        OrganizationId = organizationId, ClientId = client!.Id, ChatId = client.TelegramChatId!.Value,
                        MaintenanceId = maintenanceId, MaintenanceVersion = maintenance.Version, VisitStartsAtUtc = startsAt,
                        EventCode = MaintenanceVisitReminderRules.EventCode, NotificationKey = key, Body = body,
                        CreatedAt = clock.UtcNow, NextAttemptAt = clock.UtcNow
                    });
                }
                if (channel == "max" && !sentMax && !await context.MaxClientNotifications.AnyAsync(item => item.NotificationKey == key))
                {
                    context.MaxClientNotifications.Add(new MaxClientNotification
                    {
                        OrganizationId = organizationId, ClientId = client!.Id, MaxUserId = client.MaxUserId!.Value,
                        MaintenanceId = maintenanceId, MaintenanceVersion = maintenance.Version, VisitStartsAtUtc = startsAt,
                        EventCode = MaintenanceVisitReminderRules.EventCode, NotificationKey = key, Body = body,
                        CreatedAt = clock.UtcNow, NextAttemptAt = clock.UtcNow
                    });
                }
            }

            await context.SaveChangesAsync();
        }, () => Task.FromResult(false));
    }

    private async Task<MaintenanceEntity> LoadAsync(Guid organizationId, Guid maintenanceId)
    {
        return await context.Maintenance.AsNoTracking().Include(item => item.Vehicle).ThenInclude(item => item.OrganizationClient)
            .SingleOrDefaultAsync(item => item.Id == maintenanceId && !item.IsDeleted && !item.Vehicle.IsDeleted
                && item.Vehicle.OrganizationId == organizationId) ?? throw new KeyNotFoundException("Обслуживание не найдено.");
    }

    private async Task<List<string>> GetChannelsAsync(Organization organization, OrganizationClient? client)
    {
        if (client == null || client.IsDeleted
            || !await subscriptions.HasFeatureAsync(organization.OwnerUserId, organization.Id, Helpers.FeatureCodes.ClientMessaging))
        {
            return [];
        }

        var telegram = configuration.GetValue<bool>("TelegramOrganizationNotifications:Enabled")
            && client.TelegramNotificationsEnabled && client.TelegramChatId > 0 && client.TelegramConnectedAt.HasValue
            && await context.OrganizationTelegramBots.AsNoTracking().AnyAsync(item => item.OrganizationId == organization.Id && item.IsEnabled && item.NotifyBooking);
        var max = configuration.GetValue<bool>("MaxNotifications:Enabled") && client.MaxNotificationsEnabled && client.MaxUserId > 0
            && await context.OrganizationMaxBots.AsNoTracking().AnyAsync(item => item.OrganizationId == organization.Id && item.IsEnabled && item.NotifyBooking);
        var channels = new List<string>();
        if (telegram && MaintenanceVisitReminderRules.IsChannelEnabled(organization.VisitReminderChannelCode, "telegram", telegram))
        {
            channels.Add("telegram");
        }
        if (max && MaintenanceVisitReminderRules.IsChannelEnabled(organization.VisitReminderChannelCode, "max", telegram))
        {
            channels.Add("max");
        }

        return channels;
    }
}
