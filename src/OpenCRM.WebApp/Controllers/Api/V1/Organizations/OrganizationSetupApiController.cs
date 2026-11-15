using OpenCRM.Modules.Organizations.Contracts.Dtos;
using OpenCRM.Modules.Organizations.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace OpenCRM.WebApp.Controllers.Api.V1.Organizations;

/// <summary> API начальной настройки организации. </summary>
[ApiController]
[Authorize]
[Route("api/v1/organizations/{organizationId:guid}/setup")]
public sealed class OrganizationSetupApiController(
    IOrganizationSetupService organizationService) : ControllerBase
{
    /// <summary> Возвращает состояние и данные настройки организации. </summary>
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
            return Ok(await organizationService.GetSetupAsync(userId, organizationId));
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    /// <summary> Открывает процесс настройки организации. </summary>
    [HttpPost("open")]
    public async Task<IActionResult> Open(Guid organizationId)
    {
        var userId = this.GetApiUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        try
        {
            return Ok(new { shouldOpen = await organizationService.TryOpenSetupAsync(userId, organizationId) });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    /// <summary> Сохраняет данные настройки организации. </summary>
    [HttpPut]
    public async Task<IActionResult> Save(Guid organizationId, SaveOrganizationSetupDto dto)
    {
        var userId = this.GetApiUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        try
        {
            await organizationService.SaveSetupAsync(userId, organizationId, dto);
            return Ok(await organizationService.GetSetupAsync(userId, organizationId));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "Настройка уже изменена. Откройте настройки заново — сохранённые данные останутся." });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }
}
