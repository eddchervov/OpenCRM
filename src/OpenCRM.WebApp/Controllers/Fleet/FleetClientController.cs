using OpenCRM.Modules.Organizations.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.WebApp.Infrastructure;

namespace OpenCRM.WebApp.Controllers.Fleet;

// HIDDEN_FLEET: сохранено для прежних организаций и согласованного возврата. См. docs/hidden-fleet-scenario.md.
/// <summary> Контроллер клиентского веб-приложения для сценария автопарка. </summary>
[Authorize]
public sealed class FleetClientController(
    UserManager<ApplicationUser> userManager,
    IOrganizationQueryService organizationService)
    : Controller
{
    /// <summary> Отображает клиентский интерфейс автопарка организации. </summary>
    [HttpGet("/fleet/{organizationId:guid}")]
    [HttpGet("/fleet/{organizationId:guid}/{*path}")]
    public async Task<IActionResult> Index(Guid organizationId)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return Redirect($"/auth/login?returnUrl={Uri.EscapeDataString($"{Request.Path}{Request.QueryString}")}");
        }

        var overview = await organizationService.GetOverviewAsync(user.Id);
        var organization = overview.Organizations.FirstOrDefault(x => x.Id == organizationId);
        if (organization == null)
        {
            return NotFound();
        }

        if (!IsFleetScenario(organization.PrimaryScenarioCode))
        {
            TempData["Error"] = "Отдельный клиент доступен только для сценария \"Автопарк\".";
            return Redirect("/organizations");
        }

        CurrentOrganizationCookie.Write(Response, organizationId);

        ViewData["OrganizationId"] = organizationId.ToString("D");
        ViewData["WorkspaceUrl"] = $"/fleet/{organizationId:D}";
        ViewData["ContextUrl"] = $"/api/v1/fleet/organizations/{organizationId:D}/workspace/context";
        ViewData["SeoRobots"] = "noindex,nofollow";
        ViewData["Title"] = $"{organization.Name} - Автопарк";

        return View("/Views/FleetClient/Index.cshtml");
    }

    /// <summary> Проверяет, соответствует ли код сценария автопарку. </summary>
    private static bool IsFleetScenario(string? code)
    {
        var normalized = string.IsNullOrWhiteSpace(code)
            ? OrganizationTypeCodes.ServiceStation
            : code.Trim().ToLowerInvariant();

        return normalized == OrganizationTypeCodes.Fleet;
    }
}
