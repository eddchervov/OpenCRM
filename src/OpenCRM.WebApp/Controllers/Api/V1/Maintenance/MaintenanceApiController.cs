using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Defects;

namespace OpenCRM.WebApp.Controllers.Api.V1.Maintenance;

/// <summary> API управления записями об обслуживании. </summary>
[ApiController]
[Authorize]
public class MaintenanceApiController(
    IMaintenanceAssignmentService maintenanceAssignmentService,
    IMaintenanceCreateService maintenanceCreateService,
    IMaintenanceDeleteService maintenanceDeleteService,
    IMaintenanceUpdateService maintenanceUpdateService,
    IVehicleDefectService vehicleDefectService) : MaintenanceApiControllerBase
{
    /// <summary> Обновляет список работ и услуг обслуживания. </summary>
    [HttpPut("api/v1/maintenance/{maintenanceId:guid}/services")]
    public async Task<IActionResult> UpdateServices(Guid maintenanceId, [FromQuery] Guid organizationId, [FromBody] UpdateMaintenanceServicesDto dto)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null)
        {
            return userIdResult.Result;
        }
        var userId = userIdResult.Value!;
        try
        {
            await maintenanceUpdateService.UpdateServices(userId, organizationId, maintenanceId, dto);
            return NoContent();
        }
        catch (Exception ex) when (IsAccessDeniedError(ex))
        {
            return Forbid();
        }
        catch (Exception ex) when (IsNotFoundError(ex))
        {
            return NotFound();
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "Запись уже изменена. Обновите карточку." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary> Обновляет ставки НДС обслуживания. </summary>
    [HttpPut("api/v1/maintenance/{maintenanceId:guid}/vat-rates")]
    public async Task<IActionResult> UpdateVatRates(
        Guid maintenanceId,
        [FromQuery] Guid organizationId,
        [FromBody] UpdateMaintenanceVatRatesDto dto)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null)
        {
            return userIdResult.Result;
        }

        try
        {
            await maintenanceUpdateService.UpdateVatRates(userIdResult.Value!, organizationId, maintenanceId, dto);

            return NoContent();
        }
        catch (Exception ex) when (IsAccessDeniedError(ex))
        {
            return Forbid();
        }
        catch (Exception ex) when (IsNotFoundError(ex))
        {
            return NotFound();
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "Заказ уже изменён. Обновите карточку." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary> Назначает сотрудника на обслуживание. </summary>
    [HttpPut("api/v1/maintenance/{maintenanceId:guid}/employee")]
    public async Task<IActionResult> AssignEmployee(Guid maintenanceId, [FromQuery] Guid organizationId, [FromBody] AssignMaintenanceEmployeeDto dto)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null)
        {
            return userIdResult.Result;
        }
        var userId = userIdResult.Value!;
        try
        {
            await maintenanceAssignmentService.AssignEmployee(userId, organizationId, maintenanceId, dto);
            return NoContent();
        }
        catch (Exception ex) when (IsAccessDeniedError(ex))
        {
            return Forbid();
        }
        catch (Exception ex) when (IsNotFoundError(ex))
        {
            return NotFound();
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "Запись уже изменена. Обновите карточку." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary> Возвращает варианты дефектов для выбора. </summary>
    [HttpGet("api/v1/maintenance/{maintenanceId:guid}/defects/options")]
    public async Task<ActionResult<IEnumerable<object>>> GetDefectOptions(Guid maintenanceId, [FromQuery] Guid? organizationId = null)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;

        try
        {
            if (!organizationId.HasValue)
            {
                return Ok(Array.Empty<object>());
            }

            return Ok(await vehicleDefectService.GetOptionsForMaintenanceAsync(userId, organizationId.Value, maintenanceId));
        }
        catch (Exception ex) when (IsAccessDeniedError(ex))
        {
            return Forbid();
        }
        catch (Exception ex) when (IsNotFoundError(ex))
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary> Создаёт запись об обслуживании автомобиля. </summary>
    [HttpPost("api/v1/vehicles/{vehicleId:guid}/maintenance")]
    public async Task<ActionResult<Guid>> Create(
        Guid vehicleId,
        [FromBody] CreateMaintenanceDto dto,
        [FromQuery] Guid? organizationId = null,
        [FromQuery] string? schedulerSourceCode = null,
        [FromQuery] Guid? schedulerSourceEntityId = null,
        [FromQuery] string? schedulerTriggerKey = null)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;

        try
        {
            var maintenanceId = await maintenanceCreateService.Create(userId, organizationId, vehicleId, dto,
                new MaintenanceSchedulerLink(schedulerSourceCode, schedulerSourceEntityId, schedulerTriggerKey));

            return Ok(maintenanceId);
        }
        catch (Exception ex) when (IsAccessDeniedError(ex))
        {
            return Forbid();
        }
        catch (Exception ex) when (IsNotFoundError(ex))
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary> Обновляет запись об обслуживании. </summary>
    [HttpPut("api/v1/maintenance/{maintenanceId:guid}")]
    public async Task<IActionResult> Update(Guid maintenanceId, [FromBody] UpdateMaintenanceDto dto, [FromQuery] Guid? organizationId = null)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;

        try
        {
            await maintenanceUpdateService.Update(userId, organizationId, maintenanceId, dto);

            return Ok();
        }
        catch (Exception ex) when (IsAccessDeniedError(ex))
        {
            return Forbid();
        }
        catch (Exception ex) when (IsNotFoundError(ex))
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary> Удаляет запись об обслуживании. </summary>
    [HttpDelete("api/v1/maintenance/{maintenanceId:guid}")]
    public async Task<IActionResult> Delete(Guid maintenanceId, [FromQuery] Guid? organizationId = null)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;

        try
        {
            await maintenanceDeleteService.Delete(userId, organizationId, maintenanceId);

            return NoContent();
        }
        catch (Exception ex) when (IsAccessDeniedError(ex))
        {
            return Forbid();
        }
        catch (Exception ex) when (IsNotFoundError(ex))
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
