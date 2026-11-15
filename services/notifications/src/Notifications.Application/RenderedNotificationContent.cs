namespace Notifications.Application;

/// <summary>
/// Содержит окончательно подготовленное для провайдера сообщение.
/// </summary>
public sealed record RenderedNotificationContent(string? Subject, string Body, bool IsHtml);
