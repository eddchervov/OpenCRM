using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;

namespace OpenCRM.WebApp.Controllers.Api.V1.Maintenance;

/// <summary> API-контроллер приёмки работ по обслуживанию. </summary>
[ApiController]
[Authorize]
public class MaintenanceIntakeApiController(
    IMaintenanceIntakeService maintenanceIntakeService) : MaintenanceApiControllerBase
{
    /// <summary> Сохраняет данные приёмки для записи об обслуживании. </summary>
    [HttpPut("api/v1/maintenance/{maintenanceId:guid}/intake")]
    public async Task<IActionResult> SaveIntake(Guid maintenanceId, [FromQuery] Guid organizationId, [FromBody] SaveMaintenanceIntakeDto dto)
    {
        var userIdResult = this.GetCurrentUserId();
        if (userIdResult.Result != null)
        {
            return userIdResult.Result;
        }

        try
        {
            await maintenanceIntakeService.SaveIntake(
                userIdResult.Value!, organizationId, maintenanceId, dto);

            return this.NoContent();
        }
        catch (Exception ex) when (MaintenanceApiControllerBase.IsAccessDeniedError(ex))
        {
            return this.Forbid();
        }
        catch (Exception ex) when (MaintenanceApiControllerBase.IsNotFoundError(ex))
        {
            return this.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return this.BadRequest(new { message = ex.Message });
        }
    }
}
