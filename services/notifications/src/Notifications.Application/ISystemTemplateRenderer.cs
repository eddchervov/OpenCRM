using Notifications.Domain;

namespace Notifications.Application;

/// <summary>Формирует системные письма из версионируемых шаблонов, поставляемых с сервисом.</summary>
public interface ISystemTemplateRenderer
{
    /// <summary>Определяет ключи, зарезервированные для системных шаблонов.</summary>
    bool IsSystemTemplate(string key);

    /// <summary>Проверяет данные и формирует письмо по разрешённым приложению ключу и версии.</summary>
    Task<RenderedNotificationContent> RenderAsync(
        string applicationId,
        string key,
        NotificationChannel channel,
        int version,
        string dataJson,
        CancellationToken cancellationToken);
}
