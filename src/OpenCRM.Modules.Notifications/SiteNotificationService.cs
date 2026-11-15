using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Notifications;
using OpenCRM.Services.Interfaces.Notifications.Dtos;

namespace OpenCRM.Modules.Notifications;

/// <summary> Сервис site-уведомлений для пользователей. </summary>
public sealed class SiteNotificationService(OpenCRMContext context, Helpers.ProjectBrand? brand = null) : ISiteNotificationService
{
    /// <summary> Зависимость доступа к данным приложения. </summary>
    private readonly OpenCRMContext _context = context;

    /// <summary> Возвращает доступные пользователю site-уведомления. </summary>
    public async Task<IReadOnlyList<SiteNotificationDto>> GetAsync(
        string userId,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        var welcomeTitle = $"Добро пожаловать в {brand?.DisplayName ?? Helpers.ProjectBrand.DefaultName}";
        var audienceMask = await this.GetAudienceMaskAsync(userId, isAdmin, cancellationToken);

        return await this.GetAvailableNotifications(userId, audienceMask)
            .OrderByDescending(notification => notification.CreatedAt)
            .Select(notification => new SiteNotificationDto(
                notification.Id,
                notification.TypeCode == SiteNotificationTypes.Welcome ? welcomeTitle : notification.Title,
                notification.Body,
                notification.CreatedAt,
                this._context.SiteNotificationClosures
                    .Where(closure => closure.NotificationId == notification.Id && closure.UserId == userId)
                    .Select(closure => (DateTime?)closure.ClosedAt)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);
    }

    /// <summary> Закрывает site-уведомление для пользователя. </summary>
    public async Task<bool> CloseAsync(
        string userId,
        bool isAdmin,
        Guid notificationId,
        CancellationToken cancellationToken)
    {
        var audienceMask = await this.GetAudienceMaskAsync(userId, isAdmin, cancellationToken);

        var isAvailable = await this.GetAvailableNotifications(userId, audienceMask)
            .AnyAsync(notification => notification.Id == notificationId, cancellationToken);

        if (!isAvailable)
        {
            return false;
        }

        await this._context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "SiteNotificationClosures" ("NotificationId", "UserId", "ClosedAt")
            VALUES ({notificationId}, {userId}, {DateTime.UtcNow})
            ON CONFLICT ("NotificationId", "UserId") DO NOTHING
            """,
            cancellationToken);

        return true;
    }

    /// <summary> Возвращает запрос доступных уведомлений для аудитории. </summary>
    private IQueryable<SiteNotification> GetAvailableNotifications(string userId, int audienceMask)
    {
        return this._context.SiteNotifications.AsNoTracking()
            .Where(notification => !notification.IsDeleted && !notification.IsTemplate &&
                (notification.RecipientUserId == userId ||
                    (notification.RecipientUserId == null && this._context.Users.Any(user =>
                        user.Id == userId && user.DatetimeCreation <= notification.CreatedAt))) &&
                (notification.AudienceMask & audienceMask) != 0);
    }

    /// <summary> Вычисляет маску аудитории пользователя. </summary>
    private async Task<int> GetAudienceMaskAsync(
        string userId,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        var audienceMask = SiteNotificationAudiences.All;
        if (isAdmin)
        {
            audienceMask |= SiteNotificationAudiences.Admins;
        }

        if (await this._context.Organizations.AsNoTracking()
            .AnyAsync(organization => organization.OwnerUserId == userId && !organization.IsDeleted,
                cancellationToken))
        {
            audienceMask |= SiteNotificationAudiences.OrganizationOwners;
        }

        return audienceMask;
    }
}
