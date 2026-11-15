using Notifications.Contracts;
using Notifications.Domain;

namespace Notifications.Application;

/// <summary>Регистрирует неизменяемые версии шаблонов в границах приложения.</summary>
public sealed class TemplateApplicationService(
    INotificationTemplateRepository templateRepository,
    ITemplateRenderer renderer,
    IIntegrationEventPublisher publisher,
    IUnitOfWork unitOfWork,
    ISystemClock clock,
    ISystemTemplateRenderer systemTemplates)
{
    /// <summary>Повторно подтверждает то же содержимое, но запрещает перезапись существующей версии.</summary>
    public async Task UpsertAsync(
        string applicationId,
        string key,
        UpsertTemplateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Channel);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BodyTemplate);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.Version, 1);
        applicationId = applicationId.Trim();
        key = key.Trim();
        if (applicationId.Length > 100 || key.Length > 200 || request.SubjectTemplate?.Length > 1000)
        {
            throw new ArgumentException("Template fields exceed the allowed length.");
        }

        if (systemTemplates.IsSystemTemplate(key))
        {
            throw new ArgumentException("System templates are deployed with the Worker and cannot be registered through RabbitMQ.");
        }

        var channel = NotificationMapping.ParseChannel(request.Channel);
        if (channel == NotificationChannel.Sms && request.IsHtml)
        {
            throw new ArgumentException("SMS templates cannot contain HTML.");
        }

        renderer.Validate(request.BodyTemplate);
        if (request.SubjectTemplate != null)
        {
            renderer.Validate(request.SubjectTemplate);
            NotificationContentResolver.ValidateSubject(request.SubjectTemplate);
        }

        var template = await templateRepository.GetAsync(
            applicationId, key, channel, cancellationToken, request.Version);

        if (template == null)
        {
            template = new NotificationTemplate(
                applicationId, key, channel, request.SubjectTemplate,
                request.BodyTemplate, request.IsHtml, clock.UtcNow, request.Version);
            templateRepository.Add(template);
        }
        else if (!HasSameContent(template, request))
        {
            throw new ArgumentException("Template version already exists with different content. Use a new version.");
        }

        await publisher.SendToApplicationAsync(
            applicationId,
            new NotificationTemplateUpdated(
                Guid.NewGuid(), applicationId, key, request.Channel.Trim().ToLowerInvariant(), clock.UtcNow,
                request.Version),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary> Проверяет совпадение содержимого шаблона с запросом. </summary>
    private static bool HasSameContent(NotificationTemplate template, UpsertTemplateRequest request)
    {
        return template.SubjectTemplate == request.SubjectTemplate
            && template.BodyTemplate == request.BodyTemplate
            && template.IsHtml == request.IsHtml;
    }
}
