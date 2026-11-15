namespace Notifications.Contracts;

/// <summary>Регистрирует неизменяемую версию шаблона приложения, которому принадлежит входная очередь.</summary>
/// <param name="Key">Ключ шаблона в пределах приложения и канала.</param>
/// <param name="Template">Содержимое шаблона.</param>
public sealed record UpsertNotificationTemplate(string Key, UpsertTemplateRequest Template);
