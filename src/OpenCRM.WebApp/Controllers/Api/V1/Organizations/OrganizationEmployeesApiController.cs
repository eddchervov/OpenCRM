using OpenCRM.Modules.Organizations.Contracts.Dtos;
using OpenCRM.Modules.Organizations.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.WebApp.Controllers.Api.V1.Organizations;

/// <summary> API-контроллер управления сотрудниками организации. </summary>
[ApiController]
[Authorize]
[AutoValidateAntiforgeryToken]
[Route("api/v1/organizations/{organizationId:guid}/employees")]
public class OrganizationEmployeesApiController(
    IOrganizationEmployeeService employeeService,
    IOrganizationPermissionService permissionService,
    IOrganizationInvitationCommandService organizationService) : ControllerBase
{
    /// <summary> Создаёт сотрудника организации с приглашением. </summary>
    [HttpPost("with-invitation")]
    public async Task<IActionResult> CreateWithInvitation(
        Guid organizationId,
        [FromBody] CreateOrganizationEmployeeInvitationDto dto)
    {
        var userId = this.GetApiUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        try
        {
            var result = await organizationService.CreateEmployeeAndInviteAsync(userId, organizationId, dto);

            return Ok(new { employeeId = result.EmployeeId, invitationId = result.InvitationId });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (DbUpdateException)
        {
            return Conflict(new { message = "Сотрудник или приглашение уже изменены. Обновите список." });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    /// <summary> Возвращает список сотрудников организации. </summary>
    [HttpGet]
    public async Task<IActionResult> Get(Guid organizationId)
    {
        var userId = this.GetApiUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        try
        {
            var items = await employeeService.GetAsync(userId, organizationId);
            var canManage = await permissionService.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.EmployeesManage);
            var canLinkMembers = canManage && await permissionService.HasPermissionAsync(
                userId, organizationId, OrganizationPermissionCodes.MembersRolesManage);
            var members = canLinkMembers
                ? await employeeService.GetMemberOptionsAsync(userId, organizationId)
                : Array.Empty<OrganizationEmployeeMemberOptionDto>();

            return Ok(new { items, canManage, canLinkMembers, members });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    /// <summary> Создаёт или обновляет данные сотрудника организации. </summary>
    [HttpPost]
    [HttpPut("{employeeId:guid}")]
    public async Task<IActionResult> Save(Guid organizationId, [FromRoute] Guid? employeeId, [FromBody] OrganizationEmployeeDto dto)
    {
        var userId = this.GetApiUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        try
        {
            var id = await employeeService.SaveAsync(userId, organizationId, employeeId, dto);

            return Ok(new { id });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "Сотрудник уже изменён. Обновите список." });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    /// <summary> Создаёт сотрудника организации для существующего участника. </summary>
    [HttpPost("for-member")]
    public async Task<IActionResult> CreateForMember(Guid organizationId, [FromBody] CreateEmployeeForMemberDto dto)
    {
        var userId = this.GetApiUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        try
        {
            var id = await employeeService.CreateForMemberAsync(
                userId, organizationId, dto.MemberId, dto.Name, dto.Phone, dto.IsActive);

            return Ok(new { id });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (DbUpdateException)
        {
            return Conflict(new { message = "Участник уже связан с карточкой сотрудника. Обновите список." });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    /// <summary> Связывает сотрудника организации с участником. </summary>
    [HttpPut("{employeeId:guid}/member")]
    public async Task<IActionResult> LinkMember(
        Guid organizationId,
        Guid employeeId,
        [FromBody] LinkEmployeeMemberDto dto)
    {
        var userId = this.GetApiUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        try
        {
            await employeeService.LinkMemberAsync(userId, organizationId, employeeId, dto.MemberId, dto.Version);

            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "Сотрудник уже изменён. Обновите список." });
        }
        catch (DbUpdateException)
        {
            return Conflict(new { message = "Участник уже связан с другим сотрудником или доступ был изменён." });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    /// <summary> DTO для связывания сотрудника с участником с проверкой версии. </summary>
    public sealed record LinkEmployeeMemberDto(Guid? MemberId, uint Version);

    /// <summary> DTO для создания сотрудника из существующего участника. </summary>
    public sealed record CreateEmployeeForMemberDto(
        Guid MemberId,
        string Name,
        string? Phone,
        bool IsActive = true);

}
