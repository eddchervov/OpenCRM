using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Notifications.Application;
using Notifications.Domain;
using Notifications.Infrastructure.Templates.OpenCRM.V1.Models;
using Microsoft.Extensions.Options;
using Razor.Templating.Core;

namespace Notifications.Infrastructure.Templating;

/// <summary>Выбирает доверенный скомпилированный Razor-шаблон и его типизированную модель.</summary>
public sealed class RazorSystemTemplateRenderer(
    IRazorTemplateEngine engine,
    IOptions<SystemTemplateOptions> options) : ISystemTemplateRenderer
{
    /// <summary> Настройки JSON-сериализации для разбора данных модели. </summary>
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    /// <summary>Резервирует отдельное пространство ключей независимо от приложения и версии.</summary>
    public bool IsSystemTemplate(string key) => key.StartsWith("system/", StringComparison.Ordinal);

    /// <summary>Формирует системное письмо без чтения шаблонов из БД или входящей команды.</summary>
    public async Task<RenderedNotificationContent> RenderAsync(
        string applicationId,
        string key,
        NotificationChannel channel,
        int version,
        string dataJson,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (channel != NotificationChannel.Email
            || version != 1
            || !options.Value.Applications.TryGetValue(applicationId, out var profile)
            || !profile.Templates.Contains(key, StringComparer.Ordinal))
        {
            throw new ArgumentException("Requested system template is unavailable.");
        }

        // Пути задаются каталогом сервиса, а не конструируются из входного ключа.
        var (model, directory) = CreateModel(key, dataJson);
        var viewData = new Dictionary<string, object>
        {
            ["BrandName"] = profile.BrandName,
            ["BrandMark"] = profile.BrandMark ?? string.Empty
        };
        var subject = await engine.RenderPartialAsync($"{directory}/Subject.cshtml", model, viewData);
        var body = await engine.RenderAsync($"{directory}/Body.cshtml", model, viewData);
        cancellationToken.ThrowIfCancellationRequested();

        // Тема — обычный текст; Razor предварительно экранирует её как HTML.
        return new RenderedNotificationContent(WebUtility.HtmlDecode(subject).Trim(), body, IsHtml: true);
    }

    /// <summary> Проверяет, что ключ входит в набор поддерживаемых системных шаблонов. </summary>
    internal static bool IsSupportedTemplate(string key)
    {
        return key is "system/email-confirmation" or "system/password-reset" or "system/subscription-expiring"
            or "system/login-code" or "system/password-reset-mobile" or "system/support-message" or "system/subscription-notice" or "system/custom-email";
    }

    /// <summary> Создаёт типизированную модель шаблона из JSON-данных и каталог шаблона. </summary>
    private static (object Model, string Directory) CreateModel(string key, string dataJson)
    {
        switch (key)
        {
            case "system/email-confirmation":
                var confirmation = Deserialize<EmailConfirmationModel>(dataJson);
                ValidateUrl(confirmation.ConfirmationUrl);

                return (confirmation, "/Templates/OpenCRM/V1/EmailConfirmation");

            case "system/password-reset":
                var passwordReset = Deserialize<PasswordResetModel>(dataJson);
                ValidateUrl(passwordReset.ResetUrl);

                return (passwordReset, "/Templates/OpenCRM/V1/PasswordReset");

            case "system/subscription-expiring":
                var subscription = Deserialize<SubscriptionExpiringModel>(dataJson);
                ValidateUrl(subscription.RenewalUrl);
                if (subscription.EndDate == DateOnly.MinValue)
                {
                    throw new ArgumentException("Subscription end date is required.");
                }

                return (subscription, "/Templates/OpenCRM/V1/SubscriptionExpiring");

            case "system/custom-email":
                return (Deserialize<CustomEmailModel>(dataJson), "/Templates/OpenCRM/V1/CustomEmail");

            case "system/login-code":
                return (Deserialize<LoginCodeModel>(dataJson), "/Templates/OpenCRM/V1/LoginCode");

            case "system/password-reset-mobile":
                var mobileReset = Deserialize<MobilePasswordResetModel>(dataJson);
                ValidateUrl(mobileReset.ResetUrl);

                return (mobileReset, "/Templates/OpenCRM/V1/MobilePasswordReset");

            case "system/support-message":
                return (Deserialize<SupportMessageModel>(dataJson), "/Templates/OpenCRM/V1/SupportMessage");

            case "system/subscription-notice":
                var notice = Deserialize<SubscriptionNoticeModel>(dataJson);
                ValidateUrl(notice.ActionUrl);
                if (notice.Kind != "demo-available" && notice.EndDate == DateOnly.MinValue)
                {
                    throw new ArgumentException("Subscription end date is required.");
                }

                return (notice, "/Templates/OpenCRM/V1/SubscriptionNotice");

            default:
                throw new ArgumentException("Requested system template is unavailable.");
        }
    }

    /// <summary> Разбирает JSON-данные в объект заданного типа. </summary>
    private static T Deserialize<T>(string dataJson) where T : class
    {
        try
        {
            var model = JsonSerializer.Deserialize<T>(dataJson, s_jsonOptions)
                ?? throw new ArgumentException("Template data must contain an object.");
            Validator.ValidateObject(model, new ValidationContext(model), validateAllProperties: true);

            return model;
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Template data does not match the model.", nameof(dataJson), exception);
        }
        catch (ValidationException exception)
        {
            throw new ArgumentException("Template data contains missing or invalid values.", nameof(dataJson), exception);
        }
    }

    /// <summary> Проверяет допустимость ссылки в данных модели. </summary>
    private static void ValidateUrl(string value)
    {
        if (!IsHttpUrl(value))
        {
            throw new ArgumentException("Template link must be an absolute HTTP(S) URL.");
        }
    }

    /// <summary> Проверяет, что значение является HTTP(S)-ссылкой. </summary>
    private static bool IsHttpUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            && !string.IsNullOrEmpty(uri.Host)
            && string.IsNullOrEmpty(uri.UserInfo)
            && !value.Any(char.IsControl);
    }
}
