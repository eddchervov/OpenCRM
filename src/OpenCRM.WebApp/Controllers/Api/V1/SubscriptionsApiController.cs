using OpenCRM.WebApp.Infrastructure.Security;
using OpenCRM.WebApp.Infrastructure.Api;
using OpenCRM.Modules.Billing.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Services.Interfaces.Subscriptions;
using OpenCRM.Services.Interfaces.Subscriptions.Dtos;
using OpenCRM.Services.Interfaces.Payments.Dto;

namespace OpenCRM.WebApp.Controllers.Api.V1;

/// <summary> API личных подписок пользователя. </summary>
[ApiController]
[Route("api/v1/subscriptions")]
[Authorize]
[CommercialModule]
public class SubscriptionsApiController(
    ISubscriptionService subscriptionService,
    IPersonalSubscriptionCheckoutService checkoutService,
    IPersonalSubscriptionQueryService subscriptionQueryService,
    IPersonalSubscriptionCatalogQueryService subscriptionCatalogService,
    UserManager<ApplicationUser> userManager) : ControllerBase
{
    /// <summary> Возвращает идентификатор текущего пользователя или ошибку. </summary>
    private ActionResult<string> GetCurrentUserId()
    {
        var userId = this.GetApiUserId();
        return string.IsNullOrWhiteSpace(userId) ? Unauthorized() : userId;
    }

    /// <summary> Возвращает подписку текущего пользователя. </summary>
    [HttpGet("me")]
    public async Task<IActionResult> GetMySubscription()
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;

        var subscription = await subscriptionQueryService.GetUserSubscriptionAsync(userId);
        return Ok(new
        {
            HasActiveSubscription = await subscriptionQueryService.HasActiveSubscriptionAsync(userId),
            Subscription = subscription
        });
    }

    /// <summary> Возвращает доступные тарифные планы с фильтром по типу. </summary>
    [HttpGet("plans")]
    public async Task<IActionResult> GetPlans([FromQuery] string? planType = null)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;

        var plans = await subscriptionCatalogService.GetCatalogPlansAsync(userId);
        return Ok(FilterPlansByType(plans, planType)
            .OrderBy(plan => plan.PlanTypeCode)
            .ThenBy(plan => plan.Order)
            .ThenBy(plan => plan.EffectivePrice));
    }

    /// <summary> Возвращает обзор подписок с фильтром по типу. </summary>
    [HttpGet("overview")]
    public async Task<IActionResult> GetOverview([FromQuery] string? planType = null)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;

        var subscription = await subscriptionQueryService.GetUserSubscriptionAsync(userId);
        if (!string.IsNullOrWhiteSpace(planType) && !IsPlanOfType(subscription?.SubscriptionPlan, planType))
        {
            subscription = null;
        }

        var defaultPlan = ShouldIncludePersonalDefaultPlan(planType)
            ? await subscriptionCatalogService.GetDefaultPlanAsync()
            : null;

        return Ok(new
        {
            HasActiveSubscription = subscription?.IsActive == true,
            Subscription = subscription,
            DefaultPlan = defaultPlan,
            Plans = FilterPlansByType(await subscriptionCatalogService.GetCatalogPlansAsync(userId), planType)
                .OrderBy(plan => plan.PlanTypeCode)
                .ThenBy(plan => plan.Order)
                .ThenBy(plan => plan.EffectivePrice),
            NextPlan = await subscriptionQueryService.GetNextPlanAsync(userId),
            IsMaxPlan = await subscriptionQueryService.IsMaxPlanAsync(userId),
            ExtensionInfo = await subscriptionQueryService.GetSubscriptionExtensionInfoAsync(userId),
            DemoAvailability = await subscriptionQueryService.CheckDemoSubscriptionAvailabilityAsync(userId)
        });
    }

    /// <summary> Возвращает историю подписок с фильтром по типу. </summary>
    [HttpGet("history")]
    public async Task<IActionResult> GetHistory([FromQuery] string? planType = null)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;

        var history = await subscriptionQueryService.GetCombinedSubscriptionHistoryAsync(userId);
        return Ok(FilterHistoryByType(history, planType)
            .OrderByDescending(item => item.CreatedAt));
    }

    /// <summary> Фильтрует тарифные планы по типу. </summary>
    private static IEnumerable<SubscriptionPlanDto> FilterPlansByType(IEnumerable<SubscriptionPlanDto> plans, string? planType)
    {
        var activePlans = plans.Where(plan => plan.IsActive);
        if (string.IsNullOrWhiteSpace(planType))
        {
            return activePlans;
        }

        var normalizedPlanType = SubscriptionPlanCatalogCodes.NormalizePlanType(planType);
        return activePlans.Where(plan => SubscriptionPlanCatalogCodes.NormalizePlanType(plan.PlanTypeCode) == normalizedPlanType);
    }

    /// <summary> Определяет, относится ли план к указанному типу. </summary>
    private static bool IsPlanOfType(SubscriptionPlanDto? plan, string planType)
    {
        if (plan == null)
        {
            return false;
        }

        return SubscriptionPlanCatalogCodes.NormalizePlanType(plan.PlanTypeCode) ==
            SubscriptionPlanCatalogCodes.NormalizePlanType(planType);
    }

    /// <summary> Определяет, нужно ли включить личный план по умолчанию. </summary>
    private static bool ShouldIncludePersonalDefaultPlan(string? planType)
    {
        return string.IsNullOrWhiteSpace(planType) ||
            SubscriptionPlanCatalogCodes.NormalizePlanType(planType) == SubscriptionPlanCatalogCodes.PlanTypes.Personal;
    }

    /// <summary> Фильтрует историю подписок по типу. </summary>
    private static IEnumerable<SubscriptionHistoryDto> FilterHistoryByType(IEnumerable<SubscriptionHistoryDto> history, string? planType)
    {
        if (string.IsNullOrWhiteSpace(planType))
        {
            return history;
        }

        var normalizedPlanType = SubscriptionPlanCatalogCodes.NormalizePlanType(planType);
        return history.Where(item =>
            item.SubscriptionPlan != null &&
            SubscriptionPlanCatalogCodes.NormalizePlanType(item.SubscriptionPlan.PlanTypeCode) == normalizedPlanType);
    }

    /// <summary> Покупает личную подписку текущего пользователя. </summary>
    [HttpPost("purchase")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(OpenCRM.WebApp.Infrastructure.Security.RateLimitPolicyNames.Payments)]
    public async Task<IActionResult> Purchase([FromBody] ApiPurchaseSubscriptionRequest request)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;

        var user = await userManager.FindByIdAsync(userId);
        if (user?.CanUseTestSubscriptions == true)
        {
            var testResult = await subscriptionService.ActivateSubscriptionForTestingAsync(userId, request.PlanId);

            return testResult.Success ? Ok(testResult) : BadRequest(testResult);
        }

        if (!CheckoutHttpResponse.TryGetOperationId(this.HttpContext, out var operationId))
        {
            return this.BadRequest(new { message = "Idempotency-Key должен содержать UUID операции.", code = "Invalid" });
        }

        var result = await checkoutService.PurchaseAsync(userId, request.PlanId, request.Buyer, operationId, request.ReturnUrl);

        return CheckoutHttpResponse.Create(result, operationId);
    }

    /// <summary> Продлевает личную подписку текущего пользователя. </summary>
    [HttpPost("extend")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(OpenCRM.WebApp.Infrastructure.Security.RateLimitPolicyNames.Payments)]
    public async Task<IActionResult> Extend([FromBody] ApiExtendSubscriptionRequest? request = null)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;

        var user = await userManager.FindByIdAsync(userId);
        if (user?.CanUseTestSubscriptions == true)
        {
            var testResult = await subscriptionService.ExtendSubscriptionForTestingAsync(userId);

            return testResult.Success ? Ok(testResult) : BadRequest(testResult);
        }

        if (!CheckoutHttpResponse.TryGetOperationId(this.HttpContext, out var operationId))
        {
            return this.BadRequest(new { message = "Idempotency-Key должен содержать UUID операции.", code = "Invalid" });
        }

        var result = await checkoutService.ExtendAsync(userId, request?.Buyer, operationId, request?.ReturnUrl);

        return CheckoutHttpResponse.Create(result, operationId);
    }

    /// <summary> Активирует демо-подписку текущего пользователя. </summary>
    [HttpPost("demo")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(OpenCRM.WebApp.Infrastructure.Security.RateLimitPolicyNames.Payments)]
    public async Task<IActionResult> ActivateDemo()
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;

        var result = await subscriptionService.GrantDemoSubscriptionAsync(userId);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary> Запрос на покупку подписки. </summary>
    public sealed class ApiPurchaseSubscriptionRequest
    {
        /// <summary> Идентификатор тарифного плана. </summary>
        public int PlanId { get; init; }

        /// <summary> Адрес возврата после оплаты. </summary>
        public string? ReturnUrl { get; init; }

        /// <summary> Данные покупателя для оплаты. </summary>
        public PaymentBuyerDto? Buyer { get; init; }
    }

    /// <summary> Запрос на продление подписки. </summary>
    public sealed class ApiExtendSubscriptionRequest
    {
        /// <summary> Адрес возврата после оплаты. </summary>
        public string? ReturnUrl { get; init; }

        /// <summary> Данные покупателя для оплаты. </summary>
        public PaymentBuyerDto? Buyer { get; init; }
    }
}
