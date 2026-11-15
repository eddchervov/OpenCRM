using System.Text.Json;

namespace Notifications.Contracts;

/// <summary>
/// Описывает запрос клиентского приложения на создание уведомления.
/// </summary>
/// <param name="IdempotencyKey">Уникальный в пределах приложения ключ, защищающий от повторного создания уведомления.</param>
/// <param name="Channel">Строковый идентификатор канала доставки.</param>
/// <param name="Recipient">Адрес почты, номер телефона, логический адресат Telegram или получатель MAX вида &lt;organizationGuidN&gt;:&lt;maxUserId&gt;.</param>
/// <param name="TemplateKey">Ключ шаблона. Необязателен, если передано готовое тело сообщения.</param>
/// <param name="Data">Значения переменных, подставляемых в шаблон.</param>
/// <param name="Subject">Готовая тема без шаблона; при заданном TemplateKey оставляется null.</param>
/// <param name="Body">Обычный текст без шаблона; для HTML используется TemplateKey и Body=null.</param>
/// <param name="ScheduledAt">Момент отложенной отправки в UTC; отсутствие значения означает немедленную отправку.</param>
/// <param name="TemplateVersion">Точная версия шаблона; для прежних команд используется версия 1.</param>
public sealed record CreateNotificationRequest(
    string IdempotencyKey,
    string Channel,
    string Recipient,
    string? TemplateKey,
    IReadOnlyDictionary<string, JsonElement>? Data,
    string? Subject,
    string? Body,
    DateTimeOffset? ScheduledAt,
    int TemplateVersion = 1);
