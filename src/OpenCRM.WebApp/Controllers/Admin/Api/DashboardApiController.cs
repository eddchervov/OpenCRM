using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.WebApp.Security;

namespace OpenCRM.WebApp.Controllers.Admin.Api;

/// <summary> API данных административной панели. </summary>
[Route("api/v1/admin/dashboard")]
[ApiController]
[Authorize(Policy = AdminAuthorizationPolicies.ViewDashboard)]
public sealed class DashboardApiController(
    IAdminDashboardService adminDashboardService,
    IAuthorizationService authorizationService,
    IConfiguration configuration) : ControllerBase
{

    /// <summary> Возвращает сводку панели за указанный период. </summary>
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int periodDays = 1)
    {
        periodDays = periodDays is 1 or 7 or 30 ? periodDays : 1;
        var canViewBilling = configuration.GetValue("PlatformPolicy:Commercial:Enabled", false)
            && (await authorizationService.AuthorizeAsync(
            User,
            AdminAuthorizationPolicies.ViewBilling)).Succeeded;

        var dashboard = await adminDashboardService.GetAsync(periodDays, canViewBilling);

        return Ok(dashboard);
    }
}
