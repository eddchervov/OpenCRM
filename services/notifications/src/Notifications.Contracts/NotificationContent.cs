namespace Notifications.Contracts;

/// <summary>Неизменяемое содержимое уведомления для журнала исходного приложения.</summary>
public sealed record NotificationContent(string? Subject, string Body, bool IsHtml, string TextBody);
