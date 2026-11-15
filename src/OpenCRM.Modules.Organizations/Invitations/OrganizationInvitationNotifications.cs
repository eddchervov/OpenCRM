using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Notifications;

namespace OpenCRM.Modules.Organizations.Invitations;

/// <summary> Site-уведомления по приглашениям в организацию. </summary>
internal sealed class OrganizationInvitationNotifications(
    OpenCRMContext context,
    ISiteNotificationPublisher publisher)
{
    /// <summary> Добавляет уведомление о принятии приглашения. </summary>
    internal Task AddAcceptedAsync(string invitedByUserId, Guid invitationId, string organizationName, string email) =>
        publisher.AddAsync(
            SiteNotificationTypes.InvitationAccepted,
            invitedByUserId,
            SiteNotificationAudiences.Admins | SiteNotificationAudiences.OrganizationOwners,
            $"invitation-accepted:{invitationId}",
            new Dictionary<string, string>
            {
                ["organization"] = organizationName,
                ["email"] = email
            });

    /// <summary> Добавляет уведомление о новом приглашении получателю. </summary>
    internal Task AddInvitationAsync(string recipientUserId, Guid invitationId, string invitationToken, string organizationName) =>
        publisher.AddAsync(
            SiteNotificationTypes.OrganizationInvitation,
            recipientUserId,
            SiteNotificationAudiences.All,
            $"invitation:{invitationId}:{invitationToken}",
            new Dictionary<string, string> { ["organization"] = organizationName });

    /// <summary> Закрывает уведомления по приглашению. </summary>
    internal async Task CloseAsync(Guid invitationId)
    {
        var eventKeyPrefix = $"invitation:{invitationId}:";
        var notifications = await context.SiteNotifications
            .AsNoTracking()
            .Where(notification => notification.TypeCode == SiteNotificationTypes.OrganizationInvitation
                && !notification.IsDeleted
                && notification.RecipientUserId != null
                && notification.EventKey != null
                && notification.EventKey.StartsWith(eventKeyPrefix))
            .Select(notification => new { notification.Id, notification.RecipientUserId })
            .ToListAsync();

        foreach (var notification in notifications)
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO \"SiteNotificationClosures\" (\"NotificationId\", \"UserId\", \"ClosedAt\") VALUES ({notification.Id}, {notification.RecipientUserId}, {DateTime.UtcNow}) ON CONFLICT (\"NotificationId\", \"UserId\") DO NOTHING");
        }
    }
}
