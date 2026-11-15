using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Domain.Enums;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.WebApp.Controllers.Api.V1.Maintenance;

/// <summary> API-контроллер чтения данных об обслуживании. </summary>
[ApiController]
[Authorize]
public class MaintenanceReadApiController(
    IMaintenanceQueryService maintenanceQueryService) : MaintenanceApiControllerBase
{
    /// <summary> Возвращает запись об обслуживании по идентификатору. </summary>
    [HttpGet("api/v1/maintenance/{maintenanceId:guid}")]
    public async Task<ActionResult<GetMaintenanceByIdDto>> GetById(Guid maintenanceId, [FromServices] IOrganizationPermissionService permissionService, [FromQuery] Guid? organizationId = null)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;

        try
        {
            var result = await maintenanceQueryService.GetById(userId, organizationId, maintenanceId);
            if (organizationId.HasValue)
            {
                result.CanManagePayments = await permissionService.HasPermissionAsync(userId, organizationId.Value, OrganizationPermissionCodes.FinanceManage);
                result.CanEditServices = await permissionService.HasPermissionAsync(userId, organizationId.Value, OrganizationPermissionCodes.MaintenanceEdit);
            }

            return Ok(result);
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

    /// <summary> Возвращает постраничный список обслуживаний автомобиля с фильтрами и сортировкой. </summary>
    [HttpGet("api/v1/vehicles/{vehicleId:guid}/maintenance/{page:int}/{pageSize:int}")]
    public async Task<ActionResult<MaintenanceListResponse>> Get(Guid vehicleId, int page, int pageSize, [FromQuery] string? sort = null, [FromQuery] int? type = null, [FromQuery] int? status = null, [FromQuery] Guid? organizationId = null)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;

        try
        {
            return Ok(await maintenanceQueryService.Get(userId, organizationId, vehicleId, page, pageSize, sort, type, status));
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

    /// <summary> Возвращает постраничный список удалённых обслуживаний автомобиля. </summary>
    [HttpGet("api/v1/vehicles/{vehicleId:guid}/maintenance/deleted/{page:int}/{pageSize:int}")]
    public async Task<ActionResult<MaintenanceListResponse>> GetDeleted(Guid vehicleId, int page, int pageSize, [FromQuery] Guid? organizationId = null)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;

        try
        {
            return Ok(await maintenanceQueryService.GetDeleted(userId, organizationId, vehicleId, page, pageSize));
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

    /// <summary> Возвращает данные для аналитики по обслуживаниям автомобиля. </summary>
    [HttpGet("api/v1/vehicles/{vehicleId:guid}/maintenance/analytics")]
    public async Task<ActionResult<IEnumerable<GetMaintenanceDto>>> GetAnalytics(Guid vehicleId, [FromQuery] Guid? organizationId = null)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;

        try
        {
            return Ok((await maintenanceQueryService.Get(userId, organizationId, vehicleId))
                .Where(x => x.Status == MaintenanceStatus.Completed));
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

    /// <summary> Возвращает историю событий записи об обслуживании. </summary>
    [HttpGet("api/v1/maintenance/{maintenanceId:guid}/history")]
    public async Task<ActionResult<IReadOnlyList<MaintenanceHistoryEventDto>>> GetHistory(Guid maintenanceId, [FromQuery] Guid? organizationId = null)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;

        try
        {
            return Ok(await maintenanceQueryService.GetHistory(userId, organizationId, maintenanceId));
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
