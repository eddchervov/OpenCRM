using OpenCRM.Modules.Organizations.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.WebApp.Infrastructure;

namespace OpenCRM.WebApp.Controllers.AutoService;

/// <summary> Контроллер клиентского веб-приложения для сценария автосервиса. </summary>
[AllowAnonymous]
public sealed class AutoServiceClientController(
    UserManager<ApplicationUser> userManager,
    IOrganizationQueryService organizationService)
    : Controller
{
    /// <summary> Отображает клиентский интерфейс автосервиса организации. </summary>
    [HttpGet("/autoservice/{organizationId:guid}")]
    [HttpGet("/autoservice/{organizationId:guid}/{*path}")]
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

        if (!IsAutoServiceScenario(organization.PrimaryScenarioCode))
        {
            TempData["Error"] = "Отдельный клиент доступен только для сценария \"СТО / Автосервис\".";
            return Redirect("/organizations");
        }

        CurrentOrganizationCookie.Write(Response, organizationId);

        ViewData["OrganizationId"] = organizationId.ToString("D");
        ViewData["WorkspaceUrl"] = $"/autoservice/{organizationId:D}";
        ViewData["ContextUrl"] = $"/api/v1/autoservice/organizations/{organizationId:D}/workspace/context";
        ViewData["SeoRobots"] = "noindex,nofollow";
        ViewData["Title"] = $"{organization.Name} - СТО / Автосервис";
        Response.Headers["Permissions-Policy"] = "geolocation=(), microphone=(self)";

        return View("/Views/AutoServiceClient/Index.cshtml");
    }

    /// <summary> Проверяет, соответствует ли код сценария автосервису. </summary>
    private static bool IsAutoServiceScenario(string? code)
    {
        var normalized = OrganizationScenarioProcessCatalog.NormalizeScenarioCode(code);
        return string.Equals(
            normalized,
            OrganizationTypeCodes.ServiceStation,
            StringComparison.OrdinalIgnoreCase);
    }
}
