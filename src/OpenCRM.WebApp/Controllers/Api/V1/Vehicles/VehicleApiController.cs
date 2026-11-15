using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.WebApp.Controllers.Api.V1;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Vehicles.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.WebApp.Controllers.Api.V1.Vehicles;

/// <summary> API техники. </summary>
[ApiController]
[Authorize]
public class VehicleApiController(
    IVehicleQueryService vehicleService,
    IVehicleCommandService vehicleCommands,
    IVehicleCounterQueryService counterQueries,
    IVehicleCounterCommandService counterCommands,
    IVehicleLimitQueryService vehicleLimits,
    IOrganizationPermissionService organizationPermissionService,
    IVehicleRegistrationPlatePolicy registrationPlates) : Controller
{
    /// <summary> Возвращает общие маски госномеров по типам техники. </summary>
    [HttpGet("api/v1/vehicles/registration-plate-formats")]
    public ActionResult<VehicleRegistrationPlateFormatsDto> GetRegistrationPlateFormats()
    {
        return this.Ok(registrationPlates.GetFormats());
    }

    /// <summary> Возвращает идентификатор текущего пользователя или ошибку. </summary>
    private ActionResult<string> GetCurrentUserId()
    {
        var userId = this.GetApiUserId();
        return string.IsNullOrWhiteSpace(userId) ? Unauthorized() : userId;
    }

    /// <summary> Проверяет, является ли исключение ошибкой доступа. </summary>
    private static bool IsAccessDeniedError(Exception exception)
    {
        return exception.Message.Contains("нет доступа", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("только на просмотр", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("только для чтения", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary> Проверяет, является ли исключение ошибкой превышения лимита. </summary>
    private static bool IsLimitExceededError(InvalidOperationException exception)
    {
        return exception.Message.Contains("лимит", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("limit", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary> Проверяет, является ли исключение ошибкой отсутствия сущности. </summary>
    private static bool IsNotFoundError(Exception exception)
    {
        return exception.Message.Contains("не найдена", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("не найдено", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("not found", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary> Возвращает технику по её идентификатору. </summary>
    [HttpGet("api/v1/vehicles/{vehicleId:guid}")]
    public async Task<ActionResult<GetVehicleDto>> Get(Guid vehicleId, Guid? organizationId = null)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;

        try
        {
            return Ok(await vehicleService.GetById(userId, organizationId, vehicleId));
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

    /// <summary> Возвращает историю событий по технике. </summary>
    [HttpGet("api/v1/vehicles/{vehicleId:guid}/history")]
    public async Task<ActionResult<IReadOnlyList<VehicleHistoryEventDto>>> GetHistory(Guid vehicleId, Guid? organizationId = null, int take = 120)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;

        try
        {
            return Ok(await vehicleService.GetHistory(userId, organizationId, vehicleId, take));
        }
        catch (Exception ex) when (IsAccessDeniedError(ex))
        {
            return Forbid();
        }
        catch (Exception ex) when (IsNotFoundError(ex))
        {
            return NotFound();
        }
    }

    /// <summary> Возвращает постраничный список техники с фильтрами и поиском. </summary>
    [HttpGet("api/v1/vehicles")]
    public async Task<ActionResult<GetVehiclesResponse>> Get(int page = 1, int pageSize = 6, bool isArchived = false, string? brandFilter = null, int? brandId = null, int? kind = null, string? searchQuery = null, Guid? organizationId = null, Guid? organizationClientId = null)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;
        brandFilter ??= brandId.HasValue ? $"catalog:{brandId.Value}" : null;
        var canViewMaintenance = !organizationId.HasValue
            || await organizationPermissionService.HasPermissionAsync(userId, organizationId.Value, OrganizationPermissionCodes.MaintenanceView);

        try
        {
            return Ok(await vehicleService.Get(userId, organizationId, page, pageSize, isArchived, brandFilter, kind, searchQuery, organizationClientId, canViewMaintenance));
        }
        catch (Exception ex) when (IsAccessDeniedError(ex))
        {
            return Forbid();
        }
    }

    /// <summary> Возвращает количество активной и архивной техники. </summary>
    [HttpGet("api/v1/vehicles/counts")]
    public async Task<ActionResult<VehicleCountsDto>> GetCounts(Guid? organizationId = null)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;

        try
        {
            return new VehicleCountsDto
            {
                ActiveCount = await vehicleService.GetVehiclesCountAsync(userId, organizationId),
                ArchivedCount = await vehicleService.GetArchivedVehiclesCountAsync(userId, organizationId)
            };
        }
        catch (Exception ex) when (IsAccessDeniedError(ex))
        {
            return Forbid();
        }
    }

    /// <summary> Создаёт новую запись техники. </summary>
    [HttpPost("api/v1/vehicles")]
    public async Task<ActionResult<Guid>> Create([FromBody] CreateVehicleDto dto)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;

        try
        {
            return Ok(await vehicleCommands.Create(userId, dto.OrganizationId, dto));
        }
        catch (InvalidOperationException ex) when (IsLimitExceededError(ex))
        {
            return StatusCode(402, new { message = ex.Message });
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

    /// <summary> Обновляет данные техники. </summary>
    [HttpPut("api/v1/vehicles/{vehicleId:guid}")]
    public async Task<IActionResult> Update(Guid vehicleId, [FromBody] UpdateVehicleDto dto, Guid? organizationId = null)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;

        try
        {
            await vehicleCommands.Update(userId, organizationId, vehicleId, dto);
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
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary> Удаляет запись техники. </summary>
    [HttpDelete("api/v1/vehicles/{vehicleId:guid}")]
    public async Task<IActionResult> Delete(Guid vehicleId, Guid? organizationId = null)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;

        try
        {
            await vehicleCommands.Archive(userId, organizationId, vehicleId);
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
    }

    /// <summary> Возвращает информацию о лимите техники тарифа. </summary>
    [HttpGet("api/v1/vehicles/limit-info")]
    public async Task<ActionResult<VehicleLimitInfoDto>> GetVehicleLimitInfo(Guid? organizationId = null)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        return await vehicleLimits.GetVehicleLimitInfoAsync(userIdResult.Value!, organizationId);
    }

    /// <summary> Возвращает технику из архива. </summary>
    [HttpPost("api/v1/vehicles/{vehicleId:guid}/restore")]
    public async Task<IActionResult> RestoreFromArchive(Guid vehicleId, Guid? organizationId = null)
    {
        try
        {
            var userIdResult = GetCurrentUserId();
            if (userIdResult.Result != null) return userIdResult.Result;
            var userId = userIdResult.Value!;
            await vehicleCommands.RestoreFromArchive(userId, organizationId, vehicleId);
            return Ok();
        }
        catch (InvalidOperationException ex) when (IsLimitExceededError(ex))
        {
            return StatusCode(402, new { message = ex.Message });
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

    /// <summary> Передаёт технику в организацию. </summary>
    [HttpPost("api/v1/vehicles/{vehicleId:guid}/transfer-to-organization")]
    public async Task<IActionResult> TransferToOrganization(Guid vehicleId, [FromBody] TransferVehicleToOrganizationDto dto)
    {
        try
        {
            var userIdResult = GetCurrentUserId();
            if (userIdResult.Result != null) return userIdResult.Result;
            var userId = userIdResult.Value!;
            await vehicleCommands.TransferToOrganizationAsync(userId, vehicleId, dto.OrganizationId);
            return Ok();
        }
        catch (InvalidOperationException ex) when (IsLimitExceededError(ex))
        {
            return StatusCode(402, new { message = ex.Message });
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

    /// <summary> Возвращает показание счётчика техники на дату. </summary>
    [HttpGet("api/v1/vehicles/{vehicleId:guid}/counter-entry")]
    public async Task<ActionResult<VehicleCounterLookupDto>> GetCounterEntry(Guid vehicleId, DateTime recordedAt, [FromQuery] Guid? organizationId = null)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;

        try
        {
            return Ok(await counterQueries.GetCounterEntryForDateAsync(userId, organizationId, vehicleId, recordedAt));
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

    /// <summary> Сохраняет показание счётчика техники. </summary>
    [HttpPost("api/v1/vehicles/{vehicleId:guid}/counter-entry")]
    public async Task<IActionResult> SaveCounterEntry(Guid vehicleId, [FromBody] UpdateVehicleCounterDto dto, [FromQuery] Guid? organizationId = null)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;

        try
        {
            dto.VehicleId = vehicleId;
            await counterCommands.UpdateCounterAsync(userId, organizationId, dto);
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
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary> Возвращает историю показаний счётчика техники. </summary>
    [HttpGet("api/v1/vehicles/{vehicleId:guid}/counter-history")]
    public async Task<ActionResult<IReadOnlyList<VehicleCounterEntryDto>>> GetCounterHistory(Guid vehicleId, int take = 200, [FromQuery] Guid? organizationId = null)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;

        try
        {
            return Ok(await counterQueries.GetCounterHistoryAsync(userId, organizationId, vehicleId, take));
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

    /// <summary> Количества активной и архивной техники. </summary>
    public class VehicleCountsDto
    {
        /// <summary> Количество активных записей техники. </summary>
        public int ActiveCount { get; set; }

        /// <summary> Количество архивных записей техники. </summary>
        public int ArchivedCount { get; set; }
    }
}




