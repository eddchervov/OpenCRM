using OpenCRM.Domain.Identity;

namespace OpenCRM.Domain;

/// <summary> Запись о закрытии уведомления пользователем. </summary>
public sealed class SiteNotificationClosure
{
    /// <summary> Идентификатор уведомления. </summary>
    public Guid NotificationId { get; set; }

    /// <summary> Закрытое уведомление. </summary>
    public SiteNotification Notification { get; set; } = null!;

    /// <summary> Идентификатор пользователя, закрывшего уведомление. </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary> Пользователь, закрывший уведомление. </summary>
    public ApplicationUser User { get; set; } = null!;

    /// <summary> Дата и время закрытия уведомления. </summary>
    public DateTime ClosedAt { get; set; } = DateTime.UtcNow;
}
