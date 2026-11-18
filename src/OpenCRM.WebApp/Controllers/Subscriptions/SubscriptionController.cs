using System.Globalization;
using System.Resources;
using OpenCRM.WebApp.Infrastructure.Localization;
using OpenCRM.WebApp.Infrastructure.Security;
using OpenCRM.Modules.Payments.Contracts;
using OpenCRM.WebApp.Infrastructure.Api;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Organizations.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Domain.Identity;
using OpenCRM.Domain;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Subscriptions;
using OpenCRM.Services.Interfaces.Payments.Dto;
using OpenCRM.WebApp.Navigation;
using OpenCRM.WebApp.Models.Subscriptions;
using OpenCRM.WebApp.Product;
using System.Linq;
using System.Threading.Tasks;

namespace OpenCRM.WebApp.Controllers.Subscriptions;

/// <summary> Контроллер управления подписками. </summary>
[CommercialModule]
public class SubscriptionController : Controller
{
    private static readonly ResourceManager s_resources = new(
        "OpenCRM.WebApp.Resources.Infrastructure.Localization.LocalizationStrings",
        typeof(LocalizationStrings).Assembly);

    private static string Localize(string key) =>
        SubscriptionController.s_resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;


    /// <summary> Сервис управления подписками. </summary>
    private readonly ISubscriptionService _subscriptionService;

    /// <summary> Сервис оформления покупки подписки. </summary>
    private readonly IPersonalSubscriptionCheckoutService _checkoutService;

    /// <summary> Сервис запросов каталога тарифов подписки. </summary>
    private readonly IPersonalSubscriptionCatalogQueryService _subscriptionCatalogService;

    /// <summary> Менеджер пользователей Identity. </summary>
    private readonly UserManager<ApplicationUser> _userManager;

    /// <summary> Сервис запросов данных об организациях. </summary>
    private readonly IOrganizationQueryService _organizationService;

    /// <summary> Создаёт контроллер подписок с его зависимостями. </summary>
    public SubscriptionController(
        ISubscriptionService subscriptionService,
        IPersonalSubscriptionCheckoutService checkoutService,
        IPersonalSubscriptionCatalogQueryService subscriptionCatalogService,
        UserManager<ApplicationUser> userManager,
        IOrganizationQueryService organizationService)
    {
        _subscriptionService = subscriptionService;
        this._checkoutService = checkoutService;
        this._subscriptionCatalogService = subscriptionCatalogService;
        _userManager = userManager;
        _organizationService = organizationService;
    }

    /// <summary> Отображает публичный каталог бизнес-тарифов. </summary>
    [AllowAnonymous]
    public async Task<IActionResult> Index()
    {
        var userId = User.Identity?.IsAuthenticated == true ?
            _userManager.GetUserId(User) : null;

        var groupedPlans = (await this._subscriptionCatalogService.GetCatalogPlansAsync())
            .Where(plan => !plan.IsDemoPlan)
            .GroupBy(p => string.IsNullOrWhiteSpace(p.Group) ? "Основные тарифы" : p.Group!)
            .Select(g => new OpenCRM.Services.Interfaces.Subscriptions.Dtos.SubscriptionPlanGroupDto
            {
                GroupName = g.Key,
                Plans = g.OrderBy(p => p.Order).ThenBy(p => p.EffectivePrice).ToList(),
                Order = g.Key switch
                {
                    "Популярные тарифы" => 0,
                    "Основные тарифы" => 1,
                    _ => 2
                }
            })
            .OrderBy(g => g.Order)
            .ThenBy(g => g.GroupName)
            .ToList();

        var viewModel = new SubscriptionViewModel
        {
            GroupedPlans = groupedPlans,
            BusinessPlanGroups = SubscriptionCatalogHelper.GetBusinessGroups(groupedPlans),
            HasBusinessPlans = SubscriptionCatalogHelper.HasBusinessGroups(groupedPlans),
            SuccessMessage = TempData["Success"]?.ToString(),
            ErrorMessage = TempData["Error"]?.ToString(),
            InfoMessage = TempData["Info"]?.ToString(),
            WarningMessage = TempData["Warning"]?.ToString()
        };

        if (!string.IsNullOrEmpty(userId))
        {
            await this.ApplyBusinessPlansTargetAsync(viewModel, userId);
        }

        return View(viewModel);
    }

    /// <summary> Дополняет модель страницы информацией о целевом бизнес-тарифе. </summary>
    private async Task ApplyBusinessPlansTargetAsync(SubscriptionViewModel viewModel, string userId)
    {
        var overview = await _organizationService.GetOverviewAsync(userId);
        if (overview.Organizations.Count == 1)
        {
            viewModel.BusinessPlansTargetUrl = OrganizationWorkspaceUrlHelper.Resolve(overview.Organizations[0], "billing");
            viewModel.BusinessPlansTargetText = SubscriptionController.Localize("subscription.openWorkspacePlans");
            return;
        }

        if (overview.Organizations.Count == 0)
        {
            viewModel.BusinessPlansTargetUrl = "/organizations/create";
            viewModel.BusinessPlansTargetText = SubscriptionController.Localize("subscription.createWorkspace");
            return;
        }

        viewModel.BusinessPlansTargetUrl = "/organizations";
        viewModel.BusinessPlansTargetText = SubscriptionController.Localize("subscription.selectWorkspace");
    }

    /// <summary> Активирует демо-подписку пользователя. </summary>
    [Authorize]
    [HttpPost("activate-demo")]
    [ValidateAntiForgeryToken]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(OpenCRM.WebApp.Infrastructure.Security.RateLimitPolicyNames.Payments)]
    public async Task<IActionResult> ActivateDemoSubscription()
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(userId))
        {
            return NotFound($"Unable to load user with ID '{userId}'.");
        }

        var result = await _subscriptionService.GrantDemoSubscriptionAsync(userId);

        if (!result.Success)
        {
            TempData["Error"] = result.Error;
        }
        else
        {
            TempData["Success"] = SubscriptionController.Localize("subscription.demoActivated");
            TempData["AnalyticsGoal"] = "demo_activated";
            TempData["AnalyticsParams"] = """{"scope":"personal"}""";
        }

        return RedirectToAction(nameof(Index));
    }

    /// <summary> Оформляет покупку подписки по выбранному тарифу. </summary>
    [Authorize]
    [HttpPost("purchase")]
    [ValidateAntiForgeryToken]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(OpenCRM.WebApp.Infrastructure.Security.RateLimitPolicyNames.Payments)]
    public async Task<IActionResult> PurchaseSubscription(int planId, [FromForm] PaymentBuyerDto? buyer)
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(userId))
        {
            return NotFound($"Unable to load user with ID '{userId}'.");
        }

        var user = await _userManager.GetUserAsync(User);
        if (user?.CanUseTestSubscriptions == true)
        {
            var testResult = await _subscriptionService.ActivateSubscriptionForTestingAsync(userId, planId);
            TempData[testResult.Success ? "Success" : "Error"] = testResult.Success
                ? SubscriptionController.Localize("subscription.testActivated")
                : testResult.Error;
            return RedirectToAction(nameof(Index));
        }

        if (!CheckoutHttpResponse.TryGetOperationId(this.HttpContext, out var operationId))
        {
            this.TempData["Error"] = SubscriptionController.Localize("subscription.invalidIdempotency");

            return this.RedirectToAction(nameof(Index));
        }

        var result = await this._checkoutService.PurchaseAsync(userId, planId, buyer, operationId);
        if (result.Status == PaymentCreationStatus.PaymentRequired && !string.IsNullOrEmpty(result.PaymentUrl))
        {
            return this.Redirect(result.PaymentUrl);
        }

        this.TempData[result.Status == PaymentCreationStatus.Completed ? "Success" : "Error"] =
            result.Message ?? (result.Status == PaymentCreationStatus.Completed ? SubscriptionController.Localize("subscription.paymentProcessed") : SubscriptionController.Localize("subscription.paymentIncomplete"));

        return this.RedirectToAction(nameof(Index));
    }

    /// <summary> Оформляет продление подписки. </summary>
    [Authorize]
    [HttpPost("extend")]
    [ValidateAntiForgeryToken]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(OpenCRM.WebApp.Infrastructure.Security.RateLimitPolicyNames.Payments)]
    public async Task<IActionResult> ExtendSubscription([FromForm] PaymentBuyerDto? buyer)
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(userId))
        {
            return NotFound($"Unable to load user with ID '{userId}'.");
        }

        var user = await _userManager.GetUserAsync(User);
        if (user?.CanUseTestSubscriptions == true)
        {
            var testResult = await _subscriptionService.ExtendSubscriptionForTestingAsync(userId);
            TempData[testResult.Success ? "Success" : "Error"] = testResult.Success
                ? SubscriptionController.Localize("subscription.testExtended")
                : testResult.Error;
            return RedirectToAction(nameof(Index));
        }

        if (!CheckoutHttpResponse.TryGetOperationId(this.HttpContext, out var operationId))
        {
            this.TempData["Error"] = SubscriptionController.Localize("subscription.invalidIdempotency");

            return this.RedirectToAction(nameof(Index));
        }

        var result = await this._checkoutService.ExtendAsync(userId, buyer, operationId);
        if (result.Status == PaymentCreationStatus.PaymentRequired && !string.IsNullOrEmpty(result.PaymentUrl))
        {
            return this.Redirect(result.PaymentUrl);
        }

        this.TempData[result.Status == PaymentCreationStatus.Completed ? "Success" : "Error"] =
            result.Message ?? (result.Status == PaymentCreationStatus.Completed ? SubscriptionController.Localize("subscription.paymentProcessed") : SubscriptionController.Localize("subscription.paymentIncomplete"));

        return this.RedirectToAction(nameof(Index));
    }

    /// <summary> Отменяет подписку пользователя. </summary>
    [Authorize]
    [HttpPost("cancel")]
    [ValidateAntiForgeryToken]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(OpenCRM.WebApp.Infrastructure.Security.RateLimitPolicyNames.Payments)]
    public async Task<IActionResult> CancelSubscription()
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(userId))
        {
            return NotFound($"Unable to load user with ID '{userId}'.");
        }

        var user = await _userManager.GetUserAsync(User);
        if (user?.CanUseTestSubscriptions != true)
        {
            TempData["Error"] = SubscriptionController.Localize("subscription.cancelUnavailable");
            return RedirectToAction(nameof(Index));
        }

        var result = await _subscriptionService.CancelSubscriptionForTestingAsync(userId);
        TempData[result.Success ? "Success" : "Error"] = result.Success
            ? SubscriptionController.Localize("subscription.testCanceled")
            : result.Error;

        return RedirectToAction(nameof(Index));
    }

}

