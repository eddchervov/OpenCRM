using Microsoft.AspNetCore.Localization;

namespace OpenCRM.WebApp.Infrastructure.Localization;

public sealed class UiRequestCultureProvider : RequestCultureProvider
{
    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (httpContext.Request.Cookies.TryGetValue(SupportedUiLanguages.CookieName, out var cookie)
            && CookieRequestCultureProvider.ParseCookieValue(cookie) is { } cookieCulture)
        {
            var formattingCultureName = cookieCulture.Cultures.FirstOrDefault().Value;
            var uiCultureName = cookieCulture.UICultures.FirstOrDefault().Value;
            if (SupportedUiLanguages.TryResolveFormattingCulture(formattingCultureName, out var formattingCulture)
                && SupportedUiLanguages.TryResolveCulture(uiCultureName, out var uiCulture))
            {
                return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(formattingCulture.Name, uiCulture.Name));
            }
        }

        var acceptedLanguages = httpContext.Request.GetTypedHeaders().AcceptLanguage ?? [];
        foreach (var acceptedLanguage in acceptedLanguages.OrderByDescending(language => language.Quality ?? 1))
        {
            if (acceptedLanguage.Quality == 0)
            {
                continue;
            }

            var language = acceptedLanguage.Value.Value;
            if (SupportedUiLanguages.TryResolveCulture(language, out var uiCulture))
            {
                if (!SupportedUiLanguages.TryResolveFormattingCulture(language, out var formattingCulture))
                {
                    formattingCulture = SupportedUiLanguages.GetDefaultFormattingCulture(uiCulture);
                }

                return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(formattingCulture.Name, uiCulture.Name));
            }
        }

        return Task.FromResult<ProviderCultureResult?>(null);
    }
}
