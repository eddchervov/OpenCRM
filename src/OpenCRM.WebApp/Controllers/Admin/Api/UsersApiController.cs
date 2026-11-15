using OpenCRM.WebApp.Infrastructure.Security;
using OpenCRM.Modules.Billing.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Admin.Dtos;
using OpenCRM.Services.Interfaces.Auth;
using OpenCRM.Services.Interfaces.Features;
using OpenCRM.Services.Interfaces.Features.Dtos;
using OpenCRM.WebApp.Dtos.Admin.Users;

namespace OpenCRM.WebApp.Controllers.Admin.Api;

/// <summary> API администрирования пользователей. </summary>
[Route("api/v1/admin/users")]
[ApiController]
[Authorize(Policy = AdminAuthorizationPolicies.ViewUsers)]
public class UsersApiController : ControllerBase
{
    /// <summary> Сервис администрирования пользователей. </summary>
    private readonly IAdminUsersService _adminUsersService;

    /// <summary> Сервис команд управления подписками пользователя. </summary>
    private readonly IAdminSubscriptionCommandService _subscriptionCommands;

    /// <summary> Сервис работы с токенами аутентификации. </summary>
    private readonly IAuthTokenService _authTokenService;

    /// <summary> Сервис администрирования функций. </summary>
    private readonly IFeatureAdminService _featureAdminService;

    /// <summary> Создаёт контроллер с зависимостями администрирования. </summary>
    public UsersApiController(
        IAdminUsersService adminUsersService,
        IAdminSubscriptionCommandService subscriptionCommands,
        IAuthTokenService authTokenService,
        IFeatureAdminService featureAdminService)
    {
        _adminUsersService = adminUsersService;
        this._subscriptionCommands = subscriptionCommands;
        _authTokenService = authTokenService;
        _featureAdminService = featureAdminService;
    }

    /// <summary> Возвращает постраничный список пользователей с фильтрами. </summary>
    [HttpGet]
    public async Task<GetUserResponse> Get(
        string? search = null,
        UsersSort sort = UsersSort.ByDateDesc,
        bool includeDeleted = false,
        int page = 1,
        int pageSize = 25,
        string? utmSource = null)
    {
        return await this._adminUsersService.GetAsync(search, sort, includeDeleted, page, pageSize, utmSource);
    }

    /// <summary> Возвращает содержимое организации пользователя. </summary>
    [HttpGet("{userId}/organizations/{organizationId:guid}/contents")]
    public async Task<IActionResult> GetOrganizationContents(string userId, Guid organizationId)
    {
        var contents = await this._adminUsersService.GetOrganizationContentsAsync(userId, organizationId);
        if (contents == null)
        {
            return NotFound(new { message = "Организация пользователя не найдена." });
        }

        return Ok(contents);
    }

    /// <summary> Возвращает сводку по пользователю. </summary>
    [HttpGet("{userId}/overview")]
    public async Task<IActionResult> GetOverview(string userId)
    {
        var overview = await this._adminUsersService.GetOverviewAsync(userId);
        if (overview == null)
        {
            return NotFound(new { message = "Пользователь не найден." });
        }

        return Ok(overview);
    }

    /// <summary> Обновляет доступ пользователя к тестовым подпискам. </summary>
    [HttpPut("{userId}/test-subscriptions")]
    [Authorize(Policy = AdminAuthorizationPolicies.ManageUsers)]
    [CommercialModule]
    public async Task<IActionResult> UpdateTestSubscriptionsAccess(string userId, [FromBody] UpdateUserTestSubscriptionsRequest request)
    {
        var updated = await this._adminUsersService.UpdateTestSubscriptionsAccessAsync(userId, request.CanUseTestSubscriptions);
        if (!updated)
        {
            return NotFound(new { message = "Пользователь не найден." });
        }

        return Ok(new
        {
            message = request.CanUseTestSubscriptions
                ? "Тестовое управление тарифами включено."
                : "Тестовое управление тарифами отключено."
        });
    }

    /// <summary> Выдаёт пользователю демо-подписку. </summary>
    [HttpPost("{userId}/grant-demo")]
    [Authorize(Policy = AdminAuthorizationPolicies.ManageBilling)]
    [CommercialModule]
    public async Task<IActionResult> GrantDemoSubscription(string userId)
    {
        var admin = await _authTokenService.GetCurrentUserAsync(User);
        if (admin == null)
        {
            return Unauthorized();
        }

        var result = await this._subscriptionCommands.GrantUserDemoAsync(userId, "Действие через прежний API.", admin.Id);
        if (!result.Succeeded)
        {
            return result.NotFound
                ? NotFound(new { message = result.ErrorMessage })
                : BadRequest(new { message = result.ErrorMessage });
        }

        return Ok(new { result.Message, result.EndDate });
    }

    /// <summary> Назначает пользователю подписку. </summary>
    [HttpPost("{userId}/assign-subscription")]
    [Authorize(Policy = AdminAuthorizationPolicies.ManageBilling)]
    [CommercialModule]
    public async Task<IActionResult> AssignSubscription(string userId, [FromBody] AssignUserSubscriptionRequest request)
    {
        var durationDays = await this._adminUsersService.GetSubscriptionPlanDurationDaysAsync(request.PlanId);
        if (!durationDays.HasValue)
        {
            return BadRequest(new { message = "Тариф не найден." });
        }

        var admin = await _authTokenService.GetCurrentUserAsync(User);
        if (admin == null)
        {
            return Unauthorized();
        }

        var result = await this._subscriptionCommands.GrantUserPlanAsync(
            userId, request.PlanId, durationDays.Value, "Действие через прежний API.", admin.Id);
        if (!result.Succeeded)
        {
            return result.NotFound
                ? NotFound(new { message = result.ErrorMessage })
                : BadRequest(new { message = result.ErrorMessage });
        }

        return Ok(new { result.Message, result.EndDate });
    }

    /// <summary> Возвращает пользователя на план по умолчанию. </summary>
    [HttpDelete("{userId}/active-subscription")]
    [Authorize(Policy = AdminAuthorizationPolicies.ManageBilling)]
    [CommercialModule]
    public async Task<IActionResult> ReturnToDefaultPlan(string userId)
    {
        var activeSubscription = await this._adminUsersService.GetActiveUserSubscriptionAsync(userId);
        if (activeSubscription == null)
        {
            return BadRequest(new { message = "У пользователя нет действующей подписки." });
        }

        var admin = await _authTokenService.GetCurrentUserAsync(User);
        if (admin == null)
        {
            return Unauthorized();
        }

        var result = await this._subscriptionCommands.StopSubscriptionAsync(
            "users",
            activeSubscription.Id,
            "Действие через прежний API.",
            activeSubscription.EndDate,
            admin.Id);
        if (!result.Succeeded)
        {
            return result.NotFound
                ? NotFound(new { message = result.ErrorMessage })
                : BadRequest(new { message = result.ErrorMessage });
        }

        return Ok(new { result.Message });
    }

    /// <summary> Возвращает выданные пользователю функции. </summary>
    [HttpGet("{userId}/feature-grants")]
    [CommercialModule]
    public async Task<IActionResult> GetFeatureGrants(string userId)
    {
        var grants = await _featureAdminService.GetUserFeatureGrantsAsync(userId);
        return Ok(grants);
    }

    /// <summary> Выдаёт пользователю доступ к функции. </summary>
    [HttpPost("{userId}/feature-grants")]
    [Authorize(Policy = AdminAuthorizationPolicies.ManageUsers)]
    [CommercialModule]
    public async Task<IActionResult> CreateFeatureGrant(string userId, [FromBody] CreateUserFeatureGrantDto dto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var grantedByUserId = User.Identity?.Name ?? "admin";

        try
        {
            var grant = await _featureAdminService.CreateUserFeatureGrantAsync(userId, grantedByUserId, dto);
            return Ok(new
            {
                message = "Фича успешно выдана пользователю.",
                grant
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary> Отзывает выданный доступ к функции. </summary>
    [HttpDelete("feature-grants/{grantId:int}")]
    [Authorize(Policy = AdminAuthorizationPolicies.ManageUsers)]
    [CommercialModule]
    public async Task<IActionResult> RevokeFeatureGrant(int grantId)
    {
        var result = await _featureAdminService.RevokeUserFeatureGrantAsync(grantId);
        return result
            ? Ok(new { message = "Выдача фичи отключена." })
            : NotFound(new { message = "Выдача фичи не найдена." });
    }

}
