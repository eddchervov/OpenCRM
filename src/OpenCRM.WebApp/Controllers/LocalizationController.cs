using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Domain.Identity;
using OpenCRM.WebApp.Infrastructure.Localization;

namespace OpenCRM.WebApp.Controllers;

[Route("localization")]
[AllowAnonymous]
public sealed class LocalizationController(
    UserManager<ApplicationUser> userManager,
    IAntiforgery antiforgery) : Controller
{
    [HttpGet("context")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetContext()
    {
        var user = await userManager.GetUserAsync(User);
        var locale = SupportedUiLanguages.TryResolveCulture(System.Globalization.CultureInfo.CurrentUICulture.Name, out var culture)
            ? culture.Name
            : "en-US";
        var formattingCulture = SupportedUiLanguages.TryResolveFormattingCulture(
            user?.PreferredFormattingCulture ?? System.Globalization.CultureInfo.CurrentCulture.Name,
            out var selectedFormattingCulture)
            ? selectedFormattingCulture.Name
            : SupportedUiLanguages.DefaultCultureName;
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);

        return Ok(new
        {
            locale,
            formattingCulture,
            requestVerificationToken = tokens.RequestToken
        });
    }

    [HttpPost("language")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetLanguage(string locale, string? returnUrl, string? formattingCulture = null)
    {
        if (!SupportedUiLanguages.TryResolveCulture(locale, out var culture)
            || !SupportedUiLanguages.TryResolveLanguage(locale, out var language))
        {
            return BadRequest();
        }

        if (formattingCulture is not null
            && !SupportedUiLanguages.TryResolveFormattingCulture(formattingCulture, out _))
        {
            return BadRequest();
        }

        var user = await userManager.GetUserAsync(User);
        var preferredFormattingCulture = formattingCulture ?? user?.PreferredFormattingCulture;
        var selectedFormattingCulture = culture;
        if (SupportedUiLanguages.TryResolveFormattingCulture(
            preferredFormattingCulture ?? System.Globalization.CultureInfo.CurrentCulture.Name,
            out var storedFormattingCulture))
        {
            selectedFormattingCulture = storedFormattingCulture;
        }

        if (user is not null)
        {
            user.PreferredLanguage = language;
            if (formattingCulture is not null)
            {
                user.PreferredFormattingCulture = selectedFormattingCulture.Name;
            }

            var updateResult = await userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        Response.Cookies.Append(
            SupportedUiLanguages.CookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(selectedFormattingCulture, culture)),
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Secure = Request.IsHttps
            });

        if (Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl!);
        }

        return RedirectToAction("Index", "Home");
    }
}
