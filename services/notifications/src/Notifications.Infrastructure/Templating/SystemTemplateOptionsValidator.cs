using Microsoft.Extensions.Options;
using Notifications.Contracts;

namespace Notifications.Infrastructure.Templating;

/// <summary>Отклоняет неполные профили и неизвестные ключи до начала обработки сообщений.</summary>
public sealed class SystemTemplateOptionsValidator : IValidateOptions<SystemTemplateOptions>
{
    /// <summary>Проверяет профили без привязки к конкретным приложениям.</summary>
    public ValidateOptionsResult Validate(string? name, SystemTemplateOptions options)
    {
        foreach (var (applicationId, profile) in options.Applications)
        {
            try
            {
                _ = NotificationQueues.Commands(applicationId);
            }
            catch (ArgumentException)
            {
                return ValidateOptionsResult.Fail("SystemTemplates contains an invalid application ID.");
            }

            if (!HasValidBrand(profile))
            {
                return ValidateOptionsResult.Fail($"SystemTemplates:Applications:{applicationId}:BrandName must contain 1-200 characters without control characters.");
            }

            if (!HasValidTemplates(profile.Templates))
            {
                return ValidateOptionsResult.Fail($"SystemTemplates:Applications:{applicationId}:Templates must contain distinct supported template keys.");
            }
        }

        return ValidateOptionsResult.Success;
    }

    /// <summary> Проверяет корректность имени бренда в профиле приложения. </summary>
    private static bool HasValidBrand(SystemTemplateApplicationOptions profile)
    {
        return profile != null
            && !string.IsNullOrWhiteSpace(profile.BrandName)
            && profile.BrandName.Length <= 200
            && !profile.BrandName.Any(char.IsControl);
    }

    /// <summary> Проверяет, что список шаблонов непуст и содержит допустимые ключи. </summary>
    private static bool HasValidTemplates(string[] templates)
    {
        return templates is { Length: > 0 }
            && templates.Distinct(StringComparer.Ordinal).Count() == templates.Length
            && templates.All(RazorSystemTemplateRenderer.IsSupportedTemplate);
    }
}
