using OpenCRM.Modules.Organizations.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.WebApp.Infrastructure;

namespace OpenCRM.WebApp.Controllers.Detailing;

/// <summary> Контроллер клиентского веб-приложения для сценария детейлинга. </summary>
[Authorize]
public sealed class DetailingClientController(
    UserManager<ApplicationUser> userManager,
    IOrganizationQueryService organizationService)
    : Controller
{
    /// <summary> Отображает клиентский интерфейс детейлинга организации. </summary>
    [HttpGet("/detailing/{organizationId:guid}")]
    [HttpGet("/detailing/{organizationId:guid}/{*path}")]
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

        if (!IsDetailingScenario(organization.PrimaryScenarioCode))
        {
            TempData["Error"] = "Отдельный клиент доступен только для сценария \"Детейлинг\".";
            return Redirect("/organizations");
        }

        CurrentOrganizationCookie.Write(Response, organizationId);

        ViewData["OrganizationId"] = organizationId.ToString("D");
        ViewData["WorkspaceUrl"] = $"/detailing/{organizationId:D}";
        ViewData["ContextUrl"] = $"/api/v1/detailing/organizations/{organizationId:D}/workspace/context";
        ViewData["SeoRobots"] = "noindex,nofollow";
        ViewData["Title"] = $"{organization.Name} - Детейлинг";
        Response.Headers["Permissions-Policy"] = "geolocation=(), microphone=(self)";

        return View("/Views/DetailingClient/Index.cshtml");
    }

    /// <summary> Проверяет, соответствует ли код сценария детейлингу. </summary>
    private static bool IsDetailingScenario(string? code)
    {
        var normalized = string.IsNullOrWhiteSpace(code)
            ? OrganizationTypeCodes.ServiceStation
            : code.Trim().ToLowerInvariant();

        return normalized == OrganizationTypeCodes.Detailing;
    }
}
