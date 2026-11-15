using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Admin.Dtos;

namespace OpenCRM.WebApp.Controllers.Admin.Api;

/// <summary> API управления разрешениями ролей администратора. </summary>
[Route("api/v1/admin/permissions")]
[ApiController]
[Authorize(Policy = AdminAuthorizationPolicies.ManagePermissions)]
public sealed class PermissionsApiController(IAdminPermissionService adminPermissionService) : ControllerBase
{
    /// <summary> Возвращает данные о разрешениях ролей. </summary>
    [HttpGet]
    public async Task<AdminPermissionsDto> Get()
    {
        return await adminPermissionService.GetAsync();
    }

    /// <summary> Обновляет разрешения роли по её идентификатору. </summary>
    [HttpPut("roles/{roleId}")]
    public async Task<IActionResult> UpdateRolePermissions(
        string roleId,
        [FromBody] AdminUpdateRolePermissionsDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var result = await adminPermissionService.UpdateRolePermissionsAsync(roleId, dto.PermissionCodes, userId);
        if (result.NotFound)
        {
            return NotFound(new { message = result.ErrorMessage });
        }

        if (!result.Succeeded)
        {
            return BadRequest(new { message = result.ErrorMessage });
        }

        return Ok(new { message = result.Message });
    }
}
