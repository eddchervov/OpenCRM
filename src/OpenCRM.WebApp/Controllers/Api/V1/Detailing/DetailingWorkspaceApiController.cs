using OpenCRM.Modules.Organizations.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Helpers;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.WebApp.Navigation;

namespace OpenCRM.WebApp.Controllers.Api.V1.Detailing;

/// <summary> API контекста рабочего пространства детейлинга. </summary>
[ApiController]
[Authorize]
public sealed class DetailingWorkspaceApiController(
    UserManager<ApplicationUser> userManager,
    IAuthorizationService authorizationService,
    IOrganizationTypeCatalog organizationTypeCatalog,
    IOrganizationQueryService organizationService,
    IOrganizationSubscriptionService organizationSubscriptionService,
    IOrganizationPermissionService organizationPermissionService,
    IOrganizationWorkspaceShellService organizationWorkspaceShellService,
    IOrganizationWorkspaceQueryService organizationWorkspaceQueryService,
    ILogger<DetailingWorkspaceApiController> logger)
    : ControllerBase
{
    /// <summary> Возвращает контекст рабочего пространства детейлинга организации. </summary>
    [HttpGet("/api/v1/detailing/organizations/{organizationId:guid}/workspace/context")]
    public async Task<IActionResult> LoadContext(Guid organizationId)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        var overview = await organizationService.GetOverviewAsync(user.Id);
        var organization = overview.Organizations.FirstOrDefault(x => x.Id == organizationId);
        if (organization == null)
        {
            return NotFound();
        }

        if (!IsDetailingScenario(organization.PrimaryScenarioCode))
        {
            return BadRequest(new { message = "Детейлинг-клиент доступен только для сценария \"Детейлинг\"." });
        }

        var shell = await organizationWorkspaceShellService.GetAsync(user.Id, organizationId);
        var workspaceAccess = await organizationSubscriptionService.GetWorkspaceAccessAsync(user.Id, organizationId);
        var canOpenAdminPanel = (await authorizationService.AuthorizeAsync(
            User,
            AdminAuthorizationPolicies.Access)).Succeeded;

        return Ok(new
        {
            User = new
            {
                user.Id,
                Email = user.Email ?? string.Empty,
                DisplayName = user.UserName ?? user.Email ?? string.Empty,
                CanOpenAdminPanel = canOpenAdminPanel
            },
            Organization = organization,
            WorkspaceAccess = workspaceAccess,
            CurrentSubscription = await organizationSubscriptionService.GetCurrentAsync(user.Id, organizationId),
            OrganizationSwitcher = new
            {
                Organizations = overview.Organizations.Select(x => new
                {
                    Id = x.Id.ToString("D"),
                    x.Name,
                    ScenarioName = x.PrimaryScenarioName,
                    RoleName = x.CurrentUserRoleName,
                    Url = OrganizationWorkspaceUrlHelper.ResolveBaseUrl(x),
                    IsCurrent = x.Id == organizationId
                }),
                ManageUrl = "/organizations",
                CreateUrl = "/organizations/create",
                CanCreateOrganization = CanCreateOrganization(await organizationTypeCatalog.GetOrganizationTypeOptionsAsync())
            },
            Shell = shell,
            Links = new
            {
                WorkspaceUrl = $"/detailing/{organizationId:D}",
                AdminUrl = "/admin-panel/index.html",
                SchedulerUrl = $"/detailing/{organizationId:D}#scheduler",
                QuickBookingUrl = $"/detailing/{organizationId:D}#maintenance/new",
                Api = new
                {
                    Shell = $"/api/v1/organizations/{organizationId:D}/workspace/shell",
                    Calendar = $"/api/v1/organizations/{organizationId:D}/workspace/maintenance-calendar",
                    QuickBooking = $"/api/v1/organizations/{organizationId:D}/detailing/bookings/quick"
                }
            }
        });
    }

    /// <summary> Возвращает данные рабочего дня детейлинга за дату. </summary>
    [HttpGet("/api/v1/detailing/organizations/{organizationId:guid}/workspace/workday")]
    public async Task<IActionResult> LoadWorkday(Guid organizationId, DateOnly date, string timeZoneId = "UTC")
    {
        if (date == default || date == DateOnly.MaxValue)
        {
            return BadRequest(new { message = "Укажите корректную дату рабочего дня." });
        }

        if (!TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var timeZone))
        {
            return BadRequest(new { message = "Неизвестный часовой пояс рабочего дня." });
        }

        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        var overview = await organizationService.GetOverviewAsync(user.Id);
        var organization = overview.Organizations.FirstOrDefault(item => item.Id == organizationId);
        if (organization == null || !IsDetailingScenario(organization.PrimaryScenarioCode))
        {
            return NotFound();
        }

        var snapshot = await organizationSubscriptionService.GetFeatureSnapshotAsync(user.Id, organizationId);
        var canViewWork = snapshot.Features.Any(feature => feature.BoolValue == true
            && (feature.Code == FeatureCodes.MaintenanceModule || feature.Code == FeatureCodes.VehiclesModule))
            && await organizationPermissionService.HasPermissionAsync(user.Id, organizationId, OrganizationPermissionCodes.MaintenanceView);
        if (!canViewWork)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Нет доступа к работам организации." });
        }

        var canViewClients = snapshot.Features.Any(feature => feature.Code == FeatureCodes.ClientsModule && feature.BoolValue == true)
            && await organizationPermissionService.HasPermissionAsync(user.Id, organizationId, OrganizationPermissionCodes.ClientsView);

        try
        {
            return Ok(await organizationWorkspaceQueryService.GetWorkdayAsync(
                organizationId, date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), canViewClients, timeZone));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Не удалось загрузить рабочий день детейлинга. OrganizationId: {OrganizationId}, UserId: {UserId}, TraceId: {TraceId}",
                organizationId, user.Id, HttpContext.TraceIdentifier);

            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Не удалось загрузить рабочий день. Попробуйте позже." });
        }
    }

    /// <summary> Проверяет, что код относится к сценарию детейлинга. </summary>
    private static bool IsDetailingScenario(string? code)
    {
        var normalized = string.IsNullOrWhiteSpace(code)
            ? OrganizationTypeCodes.ServiceStation
            : code.Trim().ToLowerInvariant();

        return normalized == OrganizationTypeCodes.Detailing;
    }

    /// <summary> Проверяет допустимость создания организации среди доступных типов. </summary>
    private static bool CanCreateOrganization(
        IReadOnlyList<OpenCRM.Services.Interfaces.Organizations.Dtos.OrganizationTypeOptionDto> organizationTypes)
    {
        return organizationTypes.Count > 0;
    }

}
