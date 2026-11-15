using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;

namespace OpenCRM.Modules.Maintenance;

/// <summary> Ставит уведомления об изменениях обслуживания в очередь отправки. </summary>
internal sealed class MaintenanceNotificationOutbox(
    OpenCRMContext context,
    IConfiguration? configuration = null)
{
    /// <summary> Ставит уведомление об изменениях обслуживания. </summary>
    internal async Task QueueMaxChangeAsync(Guid organizationId, MaintenanceEntity maintenance, MaintenanceSnapshot previous)
    {
        var eventCode = MaintenanceNotificationContent.GetChangeEvent(maintenance, previous);
        if (eventCode is not null)
        {
            await this.QueueMaxNotificationAsync(organizationId, maintenance, eventCode);
        }
    }

    /// <summary> Ставит уведомление по коду события обслуживания. </summary>
    internal async Task QueueMaxNotificationAsync(Guid organizationId, MaintenanceEntity maintenance, string eventCode)
    {
        var data = await (from vehicle in context.Vehicles.AsNoTracking()
            join client in context.OrganizationClients.AsNoTracking()
                on vehicle.OrganizationClientId equals client.Id
            join organization in context.Organizations.AsNoTracking()
                on vehicle.OrganizationId equals organization.Id
            join bot in context.OrganizationMaxBots.AsNoTracking()
                on organization.Id equals bot.OrganizationId
            where vehicle.Id == maintenance.VehicleId
                && organization.Id == organizationId
                && bot.IsEnabled
                && (eventCode == "ready" ? bot.NotifyReady : bot.NotifyBooking)
                && !client.IsDeleted && client.MaxNotificationsEnabled && client.MaxUserId > 0
            select new { client.Id, client.MaxUserId, organization.Name, organization.PrimaryScenarioCode, vehicle.RegistrationPlate }).SingleOrDefaultAsync();
        if (data == null)
        {
            return;
        }

        var body = MaintenanceNotificationContent.BuildBody(maintenance, data.Name, data.PrimaryScenarioCode, data.RegistrationPlate, eventCode);

        var key = $"max:{maintenance.Id:N}:{eventCode}:{maintenance.Version}";
        if (await context.MaxClientNotifications.AnyAsync(x => x.NotificationKey == key))
        {
            return;
        }

        context.MaxClientNotifications.Add(new MaxClientNotification
        {
            OrganizationId = organizationId,
            ClientId = data.Id,
            MaxUserId = data.MaxUserId!.Value,
            MaintenanceId = maintenance.Id,
            MaintenanceVersion = maintenance.Version,
            EventCode = eventCode,
            NotificationKey = key,
            Body = body
        });
        await context.SaveChangesAsync();
    }

    /// <summary> Ставит Telegram-уведомление об изменениях обслуживания. </summary>
    internal async Task QueueTelegramChangeAsync(Guid organizationId, MaintenanceEntity maintenance, MaintenanceSnapshot previous)
    {
        var eventCode = MaintenanceNotificationContent.GetChangeEvent(maintenance, previous);
        if (eventCode is not null)
        {
            await this.QueueTelegramNotificationAsync(organizationId, maintenance, eventCode);
        }
    }

    /// <summary> Ставит Telegram-уведомление по коду события обслуживания. </summary>
    internal async Task QueueTelegramNotificationAsync(Guid organizationId, MaintenanceEntity maintenance, string eventCode)
    {
        if (configuration?.GetValue<bool>("TelegramOrganizationNotifications:Enabled") != true)
        {
            return;
        }

        var data = await (from vehicle in context.Vehicles.AsNoTracking()
            join client in context.OrganizationClients.AsNoTracking()
                on vehicle.OrganizationClientId equals client.Id
            join organization in context.Organizations.AsNoTracking()
                on vehicle.OrganizationId equals organization.Id
            join bot in context.OrganizationTelegramBots.AsNoTracking()
                on organization.Id equals bot.OrganizationId
            where vehicle.Id == maintenance.VehicleId
                && organization.Id == organizationId
                && bot.IsEnabled
                && (eventCode == "ready" ? bot.NotifyReady : bot.NotifyBooking)
                && !client.IsDeleted && client.TelegramNotificationsEnabled && client.TelegramChatId > 0
            select new { client.Id, client.TelegramChatId, organization.Name, organization.PrimaryScenarioCode, vehicle.RegistrationPlate }).SingleOrDefaultAsync();
        if (data == null)
        {
            return;
        }

        var body = MaintenanceNotificationContent.BuildBody(maintenance, data.Name, data.PrimaryScenarioCode, data.RegistrationPlate, eventCode);

        var key = $"telegram-client:{maintenance.Id:N}:{eventCode}:{maintenance.Version}";
        if (await context.TelegramClientNotifications.AnyAsync(x => x.NotificationKey == key))
        {
            return;
        }

        context.TelegramClientNotifications.Add(new TelegramClientNotification
        {
            OrganizationId = organizationId,
            ClientId = data.Id,
            ChatId = data.TelegramChatId!.Value,
            MaintenanceId = maintenance.Id,
            MaintenanceVersion = maintenance.Version,
            EventCode = eventCode,
            NotificationKey = key,
            Body = body
        });
        await context.SaveChangesAsync();
    }
}
