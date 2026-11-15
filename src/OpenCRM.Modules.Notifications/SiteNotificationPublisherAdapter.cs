using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Notifications;

namespace OpenCRM.Modules.Notifications;

/// <summary> Адаптер публикации site-уведомлений для интерфейса домена. </summary>
internal sealed class SiteNotificationPublisherAdapter(OpenCRMContext context) : ISiteNotificationPublisher
{
    /// <summary> Добавляет site-уведомление через статический публикатор. </summary>
    public Task AddAsync(
        string typeCode,
        string recipientUserId,
        int audienceMask,
        string eventKey,
        IReadOnlyDictionary<string, string> values) =>
        SiteNotificationPublisher.AddAsync(context, typeCode, recipientUserId, audienceMask, eventKey, values);

    /// <summary> Добавляет уведомления после регистрации пользователя. </summary>
    public Task AddRegistrationNotificationsAsync(OpenCRM.Domain.Identity.ApplicationUser user) =>
        SiteNotificationPublisher.AddRegistrationNotificationsAsync(context, user);
}
