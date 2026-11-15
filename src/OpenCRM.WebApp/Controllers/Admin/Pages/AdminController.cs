using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Services.Interfaces.Admin;

namespace OpenCRM.WebApp.Controllers.Admin.Pages;

/// <summary> Страницы административной панели. </summary>
[Authorize(Policy = AdminAuthorizationPolicies.Access)]
public sealed class AdminController : Controller
{
    /// <summary> Открывает главную страницу панели. </summary>
    public IActionResult Index() => RedirectToAdminPanel(string.Empty);

    /// <summary> Открывает страницу пользователей. </summary>
    public IActionResult Users() => RedirectToAdminPanel("users");

    /// <summary> Открывает страницу карточки пользователя. </summary>
    [ActionName("User")]
    public IActionResult UserDetails(string? userId)
    {
        var route = string.IsNullOrWhiteSpace(userId)
            ? "users"
            : $"users/{Uri.EscapeDataString(userId)}";

        return RedirectToAdminPanel(route);
    }

    /// <summary> Открывает страницу организаций. </summary>
    public IActionResult Organizations() => RedirectToAdminPanel(string.Empty);

    /// <summary> Открывает страницу биллинга. </summary>
    public IActionResult Billing() => RedirectToAdminPanel("finance/billing");

    /// <summary> Открывает страницу вики. </summary>
    public IActionResult Wiki() => RedirectToAdminPanel(string.Empty);

    /// <summary> Открывает страницу ролей. </summary>
    public IActionResult Roles() => RedirectToAdminPanel("system/admin-roles");

    /// <summary> Открывает страницу истории SMS. </summary>
    public IActionResult SmsSendingHistories() => RedirectToAdminPanel("communications/sms");

    /// <summary> Открывает страницу email-сообщений. </summary>
    public IActionResult EmailMessages() => RedirectToAdminPanel("communications/emails");

    /// <summary> Открывает страницу тарифных планов. </summary>
    public IActionResult SubscriptionPlans() => RedirectToAdminPanel("finance/subscription-plans");

    /// <summary> Открывает страницу промокодов. </summary>
    public IActionResult PromoCodes() => RedirectToAdminPanel("finance/promo-codes");

    /// <summary> Открывает страницу функций. </summary>
    public IActionResult Features() => RedirectToAdminPanel("configuration/features");

    /// <summary> Открывает страницу каталога техники. </summary>
    public IActionResult Catalog() => RedirectToAdminPanel("configuration/vehicle-catalog");

    /// <summary> Открывает страницу предложений каталога. </summary>
    public IActionResult CatalogSuggestions() => RedirectToAdminPanel("content/catalog-suggestions");

    /// <summary> Открывает страницу тестовых данных. </summary>
    public IActionResult TestData() => RedirectToAdminPanel("system/test-data");

    /// <summary> Открывает страницу обращений в поддержку. </summary>
    public IActionResult SupportMessages() => RedirectToAdminPanel("content/support");

    /// <summary> Выполняет перенаправление на маршрут панели. </summary>
    private RedirectResult RedirectToAdminPanel(string route)
    {
        var fragment = string.IsNullOrEmpty(route) ? "/" : $"/{route}";

        return Redirect($"/admin-panel/index.html#{fragment}");
    }
}
