using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Admin.Dtos;
using OpenCRM.Services.Interfaces.Auth;

namespace OpenCRM.WebApp.Controllers.Admin.Api;

/// <summary> API получения информации о текущем пользователе-администраторе. </summary>
[Route("api/v1/admin/current-user")]
[ApiController]
[Authorize(Policy = AdminAuthorizationPolicies.Access)]
public sealed class CurrentUserApiController(
    IAuthTokenService authTokenService,
    IAdminCurrentUserService adminCurrentUserService) : ControllerBase
{
    /// <summary> Возвращает данные текущего пользователя-администратора. </summary>
    [HttpGet]
    public async Task<ActionResult<AdminCurrentUserDto>> Get()
    {
        var authenticatedUser = await authTokenService.GetCurrentUserAsync(User);
        if (authenticatedUser == null)
        {
            return Unauthorized();
        }

        var currentUser = await adminCurrentUserService.GetAsync(authenticatedUser.Id);
        if (currentUser == null)
        {
            return Unauthorized();
        }

        return Ok(currentUser);
    }
}
