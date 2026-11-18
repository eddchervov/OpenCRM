using System.Globalization;
using Microsoft.AspNetCore.Identity;
using OpenCRM.Domain.Identity;

namespace OpenCRM.WebApp.Infrastructure.Localization;

public sealed class PreferredUiCultureMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, UserManager<ApplicationUser> userManager)
    {
        context.Items[SupportedUiLanguages.RequestCultureItemKey] = CultureInfo.CurrentUICulture.Name;
        var user = await userManager.GetUserAsync(context.User);
        if (user is not null)
        {
            context.Items[SupportedUiLanguages.PersonalCurrencyItemKey] = user.PersonalCurrencyCode;
        }

        var hasPreferredLanguage = SupportedUiLanguages.TryResolveCulture(user?.PreferredLanguage, out var uiCulture);
        var hasPreferredFormattingCulture = SupportedUiLanguages.TryResolveFormattingCulture(
            user?.PreferredFormattingCulture,
            out var formattingCulture);

        // Явный язык API-клиента действует только на этот запрос и не меняет настройки пользователя или формат чисел.
        if (context.Request.Path.StartsWithSegments("/api")
            && SupportedUiLanguages.TryResolveCulture(
                context.Request.Headers[SupportedUiLanguages.RequestLanguageHeader].ToString(),
                out var requestUiCulture))
        {
            uiCulture = requestUiCulture;
            hasPreferredLanguage = true;
        }

        if (!hasPreferredLanguage && !hasPreferredFormattingCulture)
        {
            await next(context);
            return;
        }

        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        if (hasPreferredLanguage)
        {
            context.Items[SupportedUiLanguages.RequestCultureItemKey] = uiCulture.Name;
            CultureInfo.CurrentUICulture = uiCulture;
        }

        if (hasPreferredFormattingCulture)
        {
            CultureInfo.CurrentCulture = formattingCulture;
        }

        try
        {
            await next(context);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }
}
