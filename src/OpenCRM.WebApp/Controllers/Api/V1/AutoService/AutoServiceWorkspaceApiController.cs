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

namespace OpenCRM.WebApp.Controllers.Api.V1.AutoService;

/// <summary> API контекста рабочего пространства СТО. </summary>
[ApiController]
[Authorize]
public sealed class AutoServiceWorkspaceApiController(
    UserManager<ApplicationUser> userManager,
    IAuthorizationService authorizationService,
    IOrganizationTypeCatalog organizationTypeCatalog,
    IOrganizationQueryService organizationService,
    IOrganizationSubscriptionService organizationSubscriptionService,
    IOrganizationWorkspaceShellService organizationWorkspaceShellService)
    : ControllerBase
{
    /// <summary> Возвращает контекст рабочего пространства СТО организации. </summary>
    [HttpGet("/api/v1/autoservice/organizations/{organizationId:guid}/workspace/context")]
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

        if (!IsAutoServiceScenario(organization.PrimaryScenarioCode))
        {
            return BadRequest(new { message = "Клиент автосервиса доступен только для сценария \"СТО / Автосервис\"." });
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
                WorkspaceUrl = $"/autoservice/{organizationId:D}",
                AdminUrl = "/admin-panel/index.html",
                SchedulerUrl = $"/autoservice/{organizationId:D}#scheduler",
                QuickBookingUrl = $"/autoservice/{organizationId:D}#maintenance/new",
                Api = new
                {
                    Shell = $"/api/v1/organizations/{organizationId:D}/workspace/shell",
                    Calendar = $"/api/v1/organizations/{organizationId:D}/workspace/maintenance-calendar",
                    QuickBooking = $"/api/v1/organizations/{organizationId:D}/autoservice/bookings/quick"
                }
            }
        });
    }

    /// <summary> Проверяет, что код относится к сценарию СТО. </summary>
    private static bool IsAutoServiceScenario(string? code)
    {
        var normalized = OrganizationScenarioProcessCatalog.NormalizeScenarioCode(code);
        return string.Equals(
            normalized,
            OrganizationTypeCodes.ServiceStation,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary> Проверяет допустимость создания организации среди доступных типов. </summary>
    private static bool CanCreateOrganization(
        IReadOnlyList<OpenCRM.Services.Interfaces.Organizations.Dtos.OrganizationTypeOptionDto> organizationTypes)
    {
        return organizationTypes.Count > 0;
    }

}
