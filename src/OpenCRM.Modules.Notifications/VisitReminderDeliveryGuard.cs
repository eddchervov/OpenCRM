using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;

namespace OpenCRM.Modules.Notifications;

/// <summary> Повторно проверяет актуальность напоминания непосредственно перед доставкой. </summary>
internal static class VisitReminderDeliveryGuard
{
    internal static async Task<bool> IsCurrentAsync(
        OpenCRMContext context,
        IConfiguration configuration,
        Domain.Maintenance? maintenance,
        Guid organizationId,
        OrganizationClient? client,
        DateTime? expectedStart,
        string notificationKey,
        string channel,
        CancellationToken cancellationToken)
    {
        if (maintenance == null || client == null
            || !configuration.GetValue<bool>(channel == "telegram" ? "TelegramOrganizationNotifications:Enabled" : "MaxNotifications:Enabled")
            || !await context.Vehicles.AsNoTracking().AnyAsync(item => item.Id == maintenance.VehicleId
                && !item.IsDeleted && item.OrganizationId == organizationId && item.OrganizationClientId == client.Id, cancellationToken))
        {
            return false;
        }

        var organization = await context.Organizations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == organizationId, cancellationToken);
        if (organization == null || !MaintenanceVisitReminderRules.IsCurrent(maintenance, organization, expectedStart, DateTime.UtcNow)
            || notificationKey != MaintenanceVisitReminderRules.GetNotificationKey(maintenance.Id, expectedStart!.Value,
                organization.VisitReminderHoursBefore!.Value, channel))
        {
            return false;
        }

        var hasTelegram = configuration.GetValue<bool>("TelegramOrganizationNotifications:Enabled")
            && client.TelegramNotificationsEnabled && client.TelegramChatId > 0 && client.TelegramConnectedAt.HasValue
            && await context.OrganizationTelegramBots.AsNoTracking().AnyAsync(item => item.OrganizationId == organizationId
                && item.IsEnabled && item.NotifyBooking, cancellationToken);

        return MaintenanceVisitReminderRules.IsChannelEnabled(organization.VisitReminderChannelCode, channel, hasTelegram);
    }
}
