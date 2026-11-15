using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Admin.Dtos;

namespace OpenCRM.WebApp.Controllers.Admin.Api;

/// <summary> API управления ролями и их участниками. </summary>
[Route("api/v1/admin/roles")]
[ApiController]
[Authorize(Policy = AdminAuthorizationPolicies.ManagePermissions)]
public sealed class RolesApiController(IAdminRoleService adminRoleService) : ControllerBase
{
    /// <summary> Возвращает список ролей. </summary>
    [HttpGet]
    public async Task<AdminRolesDto> Get()
    {
        return await adminRoleService.GetAsync();
    }

    /// <summary> Создаёт новую роль. </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AdminRoleUpsertDto dto)
    {
        var result = await adminRoleService.CreateAsync(dto.Name);

        return MapOperationResult(result);
    }

    /// <summary> Обновляет роль по идентификатору. </summary>
    [HttpPut("{roleId}")]
    public async Task<IActionResult> Update(string roleId, [FromBody] AdminRoleUpsertDto dto)
    {
        var result = await adminRoleService.UpdateAsync(roleId, dto.Name);

        return MapOperationResult(result);
    }

    /// <summary> Удаляет роль по идентификатору. </summary>
    [HttpDelete("{roleId}")]
    public async Task<IActionResult> Delete(string roleId)
    {
        var result = await adminRoleService.DeleteAsync(roleId);

        return MapOperationResult(result);
    }

    /// <summary> Возвращает участников роли. </summary>
    [HttpGet("{roleId}/members")]
    public async Task<IActionResult> GetMembers(string roleId)
    {
        var result = await adminRoleService.GetMembersAsync(roleId, GetCurrentUserId());
        if (result.NotFound)
        {
            return NotFound(new { message = result.ErrorMessage });
        }

        return Ok(new
        {
            result.RoleId,
            result.RoleName,
            result.IsProtected,
            result.Users
        });
    }

    /// <summary> Добавляет пользователя в роль. </summary>
    [HttpPost("{roleId}/members")]
    public async Task<IActionResult> AddMember(string roleId, [FromBody] AdminRoleMemberUpdateDto dto)
    {
        var result = await adminRoleService.AddMemberAsync(roleId, dto.UserId);

        return MapOperationResult(result);
    }

    /// <summary> Удаляет пользователя из роли. </summary>
    [HttpDelete("{roleId}/members/{userId}")]
    public async Task<IActionResult> RemoveMember(string roleId, string userId)
    {
        var result = await adminRoleService.RemoveMemberAsync(roleId, userId, GetCurrentUserId());

        return MapOperationResult(result);
    }

    /// <summary> Возвращает идентификатор текущего пользователя. </summary>
    private string GetCurrentUserId()
    {
        return User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
    }

    /// <summary> Преобразует результат операции в HTTP-ответ. </summary>
    private IActionResult MapOperationResult(AdminOperationResult result)
    {
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
