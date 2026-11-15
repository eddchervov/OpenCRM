using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Domain.Identity;
using OpenCRM.Services.Interfaces.Admin;

namespace OpenCRM.WebApp.Controllers.Admin.Api;

/// <summary> API управления сессией администратора. </summary>
[Route("api/v1/admin/session")]
[ApiController]
[Authorize(Policy = AdminAuthorizationPolicies.Access)]
public sealed class AdminSessionApiController(
    SignInManager<ApplicationUser> signInManager) : ControllerBase
{
    /// <summary> Выполняет выход администратора из системы. </summary>
    [HttpPost("logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        Response.Cookies.Delete("phonenum");
        await signInManager.SignOutAsync();

        return NoContent();
    }
}
