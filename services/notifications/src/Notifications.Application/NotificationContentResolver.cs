using System.Text.Json;
using Notifications.Domain;

namespace Notifications.Application;

/// <summary>Формирует содержимое по точной версии шаблона или возвращает сохранённый снимок.</summary>
public sealed class NotificationContentResolver(
    INotificationTemplateRepository templates,
    ITemplateRenderer renderer,
    ISystemTemplateRenderer systemTemplates)
{
    /// <summary>Проверяет параметры до принятия задания и изолирует шаблоны по приложению.</summary>
    public async Task<RenderedNotificationContent> ResolveAsync(
        Notification notification,
        CancellationToken cancellationToken,
        int templateVersion = 1)
    {
        if (notification.IsHtml.HasValue)
        {
            return new RenderedNotificationContent(
                notification.Subject, notification.Body!, notification.IsHtml.Value);
        }

        if (string.IsNullOrWhiteSpace(notification.TemplateKey))
        {
            ValidateSubject(notification.Subject);

            return new RenderedNotificationContent(notification.Subject, notification.Body!, IsHtml: false);
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(templateVersion, 1);
        if (systemTemplates.IsSystemTemplate(notification.TemplateKey))
        {
            var content = await systemTemplates.RenderAsync(
                notification.ApplicationId, notification.TemplateKey, notification.Channel,
                templateVersion, notification.DataJson, cancellationToken);
            ValidateSubject(content.Subject);
            ArgumentException.ThrowIfNullOrWhiteSpace(content.Body);

            return content;
        }

        var template = await templates.GetAsync(
            notification.ApplicationId, notification.TemplateKey, notification.Channel,
            cancellationToken, templateVersion);

        if (template == null || !template.IsActive)
        {
            throw new ArgumentException("Requested template version is unavailable.");
        }

        var jsonData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(notification.DataJson) ?? [];
        var data = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in jsonData)
        {
            if (value.ValueKind is JsonValueKind.Object or JsonValueKind.Array or JsonValueKind.Null or JsonValueKind.Undefined)
            {
                throw new ArgumentException($"Template variable '{key}' must contain a scalar value.");
            }

            data.Add(key, value.ToString());
        }

        var subject = template.SubjectTemplate == null
            ? null
            : renderer.Render(template.SubjectTemplate, data, htmlEncodeValues: false);

        var body = renderer.Render(template.BodyTemplate, data, template.IsHtml);
        ValidateSubject(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        return new RenderedNotificationContent(subject, body, template.IsHtml);
    }

    /// <summary> Проверяет корректность темы уведомления. </summary>
    internal static void ValidateSubject(string? subject)
    {
        if (subject?.Length > 1000 || subject?.Any(char.IsControl) == true)
        {
            throw new ArgumentException("Subject exceeds 1000 characters or contains control characters.");
        }
    }
}
