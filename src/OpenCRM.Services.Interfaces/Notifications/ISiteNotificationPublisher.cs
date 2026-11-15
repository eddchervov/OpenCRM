using OpenCRM.Domain.Identity;

namespace OpenCRM.Services.Interfaces.Notifications;

/// <summary>Добавляет уведомления сайта в текущую единицу работы.</summary>
public interface ISiteNotificationPublisher
{
    /// <summary>Добавляет одно уведомление без отдельного сохранения.</summary>
    Task AddAsync(
        string typeCode,
        string recipientUserId,
        int audienceMask,
        string eventKey,
        IReadOnlyDictionary<string, string> values);

    /// <summary>
    /// Добавляет и сохраняет приветственное уведомление и уведомления по ожидающим приглашениям.
    /// Сохранение выполняется в текущей транзакции вызывающего сценария и не фиксирует её отдельно.
    /// </summary>
    Task AddRegistrationNotificationsAsync(ApplicationUser user);
}
