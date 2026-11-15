using System.Text.Json;

namespace Notifications.Contracts;

/// <summary>
/// Описывает регистрацию неизменяемой версии шаблона уведомления.
/// </summary>
/// <param name="Channel">Канал, для которого предназначен шаблон.</param>
/// <param name="SubjectTemplate">Шаблон темы письма; для SMS обычно не используется.</param>
/// <param name="BodyTemplate">Шаблон тела сообщения с переменными вида <c>{{name}}</c>.</param>
/// <param name="IsHtml">Указывает, что тело является HTML и значения переменных нужно экранировать.</param>
/// <param name="Version">Положительный номер версии; повтор разрешён только с тем же содержимым.</param>
public sealed record UpsertTemplateRequest(
    string Channel,
    string? SubjectTemplate,
    string BodyTemplate,
    bool IsHtml = false,
    int Version = 1);
