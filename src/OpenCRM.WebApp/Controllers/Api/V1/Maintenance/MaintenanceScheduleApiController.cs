using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using System.Globalization;

namespace OpenCRM.WebApp.Controllers.Api.V1.Maintenance;

/// <summary> API-контроллер планирования и рабочего процесса обслуживания. </summary>
[ApiController]
[Authorize]
public class MaintenanceScheduleApiController(
    IMaintenanceSchedulingService maintenanceSchedulingService,
    IMaintenanceSlotQueryService maintenanceSlotQueryService,
    IMaintenanceWorkflowService maintenanceWorkflowService) : MaintenanceApiControllerBase
{
    /// <summary> Возвращает занятые слоты обслуживания организации на дату. </summary>
    [HttpGet("api/v1/maintenance/occupied-slots")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<IReadOnlyList<OccupiedMaintenanceSlotDto>>> GetOccupiedSlots(
        [FromQuery] Guid organizationId,
        [FromQuery] DateOnly date,
        [FromQuery] string location,
        [FromQuery] Guid? excludeMaintenanceId = null,
        [FromQuery] DateOnly? dateTo = null)
    {
        var userIdResult = this.GetCurrentUserId();
        if (userIdResult.Result != null)
        {
            return userIdResult.Result;
        }

        try
        {
            return this.Ok(await maintenanceSlotQueryService.GetOccupiedSlots(
                userIdResult.Value!, organizationId, date, location, excludeMaintenanceId, dateTo));
        }
        catch (Exception ex) when (MaintenanceApiControllerBase.IsAccessDeniedError(ex))
        {
            return this.Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return this.BadRequest(new { message = ex.Message });
        }
    }

    /// <summary> Обновляет запланированные дату и время обслуживания. </summary>
    [HttpPut("api/v1/maintenance/{maintenanceId:guid}/planned-date")]
    public async Task<IActionResult> UpdatePlannedDate(Guid maintenanceId, [FromBody] MaintenancePlannedDateUpdateDto dto, [FromQuery] Guid? organizationId = null)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;

        if (!DateTime.TryParseExact(dto.PlannedDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var plannedDate))
        {
            return BadRequest(new { message = "Некорректная плановая дата." });
        }

        DateTime? plannedEndDate = null;
        if (!string.IsNullOrWhiteSpace(dto.PlannedEndDate))
        {
            if (!DateTime.TryParseExact(dto.PlannedEndDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedEndDate))
            {
                return BadRequest(new { message = "Некорректная плановая дата окончания." });
            }

            plannedEndDate = parsedEndDate;
        }

        try
        {
            await maintenanceSchedulingService.UpdatePlannedDate(userId, organizationId, maintenanceId,
                new UpdateMaintenancePlannedDateDto
                {
                    PlannedDate = plannedDate.Date,
                    PlannedEndDate = plannedEndDate,
                    PlannedStartTime = ParsePlannedTime(dto.PlannedStartTime),
                    PlannedEndTime = ParsePlannedTime(dto.PlannedEndTime)
                });

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

    /// <summary> Выполняет действие рабочего процесса над записью об обслуживании. </summary>
    [HttpPost("api/v1/maintenance/{maintenanceId:guid}/workflow")]
    public async Task<IActionResult> ChangeWorkflow(Guid maintenanceId, [FromBody] MaintenanceWorkflowActionDto dto, [FromQuery] Guid? organizationId = null)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;

        try
        {
            await maintenanceWorkflowService.ChangeWorkflow(userId, organizationId, maintenanceId, dto);

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

    /// <summary> Разбирает строку времени плана в TimeSpan. </summary>
    private static TimeSpan? ParsePlannedTime(string? value)
    {
        return TimeSpan.TryParseExact(value, new[] { @"hh\:mm", @"hh\:mm\:ss" }, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }
}
