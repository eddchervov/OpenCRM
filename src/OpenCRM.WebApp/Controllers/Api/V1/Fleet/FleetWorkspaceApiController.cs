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

namespace OpenCRM.WebApp.Controllers.Api.V1.Fleet;

// HIDDEN_FLEET: сохранено для прежних организаций и согласованного возврата. См. docs/hidden-fleet-scenario.md.
/// <summary> API контекста рабочего пространства автопарка. </summary>
[ApiController]
[Authorize]
public sealed class FleetWorkspaceApiController(
    UserManager<ApplicationUser> userManager,
    IAuthorizationService authorizationService,
    IOrganizationTypeCatalog organizationTypeCatalog,
    IOrganizationQueryService organizationService,
    IOrganizationSubscriptionService organizationSubscriptionService,
    IOrganizationWorkspaceShellService organizationWorkspaceShellService)
    : ControllerBase
{
    /// <summary> Возвращает контекст рабочего пространства автопарка организации. </summary>
    [HttpGet("/api/v1/fleet/organizations/{organizationId:guid}/workspace/context")]
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

        if (!IsFleetScenario(organization.PrimaryScenarioCode))
        {
            return BadRequest(new { message = "Клиент автопарка доступен только для сценария \"Автопарк\"." });
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
                WorkspaceUrl = $"/fleet/{organizationId:D}",
                AdminUrl = "/admin-panel/index.html",
                SchedulerUrl = $"/fleet/{organizationId:D}#scheduler",
                QuickBookingUrl = $"/fleet/{organizationId:D}#maintenance/new",
                Api = new
                {
                    Shell = $"/api/v1/organizations/{organizationId:D}/workspace/shell",
                    Calendar = $"/api/v1/organizations/{organizationId:D}/workspace/maintenance-calendar",
                    QuickBooking = $"/api/v1/vehicles"
                }
            }
        });
    }

    /// <summary> Проверяет, что код относится к сценарию автопарка. </summary>
    private static bool IsFleetScenario(string? code)
    {
        var normalized = string.IsNullOrWhiteSpace(code)
            ? OrganizationTypeCodes.ServiceStation
            : code.Trim().ToLowerInvariant();

        return normalized == OrganizationTypeCodes.Fleet;
    }

    /// <summary> Проверяет допустимость создания организации среди доступных типов. </summary>
    private static bool CanCreateOrganization(
        IReadOnlyList<OpenCRM.Services.Interfaces.Organizations.Dtos.OrganizationTypeOptionDto> organizationTypes)
    {
        return organizationTypes.Count > 0;
    }

}
