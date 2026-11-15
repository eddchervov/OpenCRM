using Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Services.Interfaces.Defects;
using OpenCRM.Services.Interfaces.Defects.Dtos;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.WebApp.Controllers.Api.V1.Organizations;

/// <summary> API-контроллер управления дефектами автомобилей организации. </summary>
[ApiController]
[Authorize]
public class OrganizationDefectsApiController(
    UserManager<ApplicationUser> userManager,
    IOrganizationSubscriptionService organizationSubscriptionService,
    IVehicleDefectService vehicleDefectService) : ControllerBase
{
    /// <summary> Возвращает постраничный список дефектов с фильтрами. </summary>
    [HttpGet("/api/v1/organizations/{organizationId:guid}/defects/{page:int}/{pageSize:int}")]
    public async Task<ActionResult<VehicleDefectListResponse>> Load(
        Guid organizationId,
        int page,
        int pageSize,
        string? searchQuery = null,
        string? statusCode = null,
        Guid? vehicleId = null,
        string? sort = null)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound();
        }

        if (!await organizationSubscriptionService.HasFeatureAsync(user.Id, organizationId, FeatureCodes.DefectsModule))
        {
            return Forbid();
        }

        try
        {
            return Ok(await vehicleDefectService.GetAsync(
                user.Id,
                organizationId,
                page,
                pageSize,
                searchQuery,
                statusCode,
                vehicleId,
                sort));
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

    /// <summary> Создаёт запись о дефекте автомобиля. </summary>
    [HttpPost("/api/v1/organizations/{organizationId:guid}/defects")]
    public async Task<ActionResult> Create(Guid organizationId, [FromBody] CreateVehicleDefectDto dto)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound();
        }

        if (!await organizationSubscriptionService.HasFeatureAsync(user.Id, organizationId, FeatureCodes.DefectsModule))
        {
            return Forbid();
        }

        try
        {
            var defectId = await vehicleDefectService.CreateAsync(user.Id, organizationId, dto);
            return Ok(new { id = defectId });
        }
        catch (Exception ex) when (IsAccessDeniedError(ex))
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary> Возвращает карточку дефекта. </summary>
    [HttpGet("/api/v1/organizations/{organizationId:guid}/defects/{defectId:guid}")]
    public async Task<ActionResult<VehicleDefectDto>> Details(Guid organizationId, Guid defectId)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound();
        }

        if (!await organizationSubscriptionService.HasFeatureAsync(user.Id, organizationId, FeatureCodes.DefectsModule))
        {
            return Forbid();
        }

        try
        {
            var defect = await vehicleDefectService.GetByIdAsync(user.Id, organizationId, defectId);
            return defect == null ? NotFound() : Ok(defect);
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

    /// <summary> Возвращает историю изменений дефекта. </summary>
    [HttpGet("/api/v1/organizations/{organizationId:guid}/defects/{defectId:guid}/history")]
    public async Task<ActionResult<IReadOnlyList<VehicleDefectHistoryItemDto>>> History(Guid organizationId, Guid defectId)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound();
        }

        if (!await organizationSubscriptionService.HasFeatureAsync(user.Id, organizationId, FeatureCodes.DefectsModule))
        {
            return Forbid();
        }

        try
        {
            return Ok(await vehicleDefectService.GetHistoryAsync(user.Id, organizationId, defectId));
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

    /// <summary> Возвращает варианты обслуживаний для связывания с дефектом. </summary>
    [HttpGet("/api/v1/organizations/{organizationId:guid}/defects/{defectId:guid}/maintenance-options")]
    public async Task<ActionResult<IReadOnlyList<VehicleDefectMaintenanceOptionDto>>> MaintenanceOptions(Guid organizationId, Guid defectId)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound();
        }

        if (!await organizationSubscriptionService.HasFeatureAsync(user.Id, organizationId, FeatureCodes.DefectsModule))
        {
            return Forbid();
        }

        try
        {
            return Ok(await vehicleDefectService.GetMaintenanceOptionsAsync(user.Id, organizationId, defectId));
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

    /// <summary> Обновляет данные дефекта. </summary>
    [HttpPut("/api/v1/organizations/{organizationId:guid}/defects/{defectId:guid}")]
    public async Task<ActionResult> Update(Guid organizationId, Guid defectId, [FromBody] UpdateVehicleDefectDto dto)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound();
        }

        if (!await organizationSubscriptionService.HasFeatureAsync(user.Id, organizationId, FeatureCodes.DefectsModule))
        {
            return Forbid();
        }

        try
        {
            await vehicleDefectService.UpdateAsync(user.Id, organizationId, defectId, dto);
            return Ok(new { message = "Неисправность сохранена." });
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

    /// <summary> Обновляет статус дефекта. </summary>
    [HttpPost("/api/v1/organizations/{organizationId:guid}/defects/{defectId:guid}/status")]
    public async Task<ActionResult> UpdateStatus(Guid organizationId, Guid defectId, [FromBody] UpdateDefectStatusRequest request)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound();
        }

        if (!await organizationSubscriptionService.HasFeatureAsync(user.Id, organizationId, FeatureCodes.DefectsModule))
        {
            return Forbid();
        }

        try
        {
            await vehicleDefectService.UpdateStatusAsync(user.Id, organizationId, defectId, request.StatusCode);
            return Ok(new { message = "Статус замечания обновлен." });
        }
        catch (Exception ex) when (IsAccessDeniedError(ex))
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary> Связывает дефект с записью об обслуживании. </summary>
    [HttpPost("/api/v1/organizations/{organizationId:guid}/defects/{defectId:guid}/maintenance/{maintenanceId:guid}")]
    public async Task<ActionResult> LinkMaintenance(Guid organizationId, Guid defectId, Guid maintenanceId)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound();
        }

        if (!await organizationSubscriptionService.HasFeatureAsync(user.Id, organizationId, FeatureCodes.DefectsModule))
        {
            return Forbid();
        }

        try
        {
            await vehicleDefectService.LinkMaintenanceAsync(user.Id, organizationId, defectId, maintenanceId);
            return Ok(new { message = "Обслуживание привязано к неисправности." });
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

    /// <summary> Отвязывает дефект от записи об обслуживании. </summary>
    [HttpDelete("/api/v1/organizations/{organizationId:guid}/defects/{defectId:guid}/maintenance/{maintenanceId:guid}")]
    public async Task<ActionResult> UnlinkMaintenance(Guid organizationId, Guid defectId, Guid maintenanceId)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound();
        }

        if (!await organizationSubscriptionService.HasFeatureAsync(user.Id, organizationId, FeatureCodes.DefectsModule))
        {
            return Forbid();
        }

        try
        {
            await vehicleDefectService.UnlinkMaintenanceAsync(user.Id, organizationId, defectId, maintenanceId);
            return Ok(new { message = "Связь с обслуживанием удалена." });
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

    /// <summary> Удаляет запись о дефекте. </summary>
    [HttpDelete("/api/v1/organizations/{organizationId:guid}/defects/{defectId:guid}")]
    public async Task<ActionResult> Delete(Guid organizationId, Guid defectId)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound();
        }

        if (!await organizationSubscriptionService.HasFeatureAsync(user.Id, organizationId, FeatureCodes.DefectsModule))
        {
            return Forbid();
        }

        try
        {
            await vehicleDefectService.DeleteAsync(user.Id, organizationId, defectId);
            return Ok(new { message = "Неисправность удалена." });
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

    /// <summary> Определяет, является ли исключение ошибкой отказа в доступе. </summary>
    private static bool IsAccessDeniedError(Exception exception)
    {
        return exception.Message.Contains("нет доступа", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("not allowed", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("access", StringComparison.OrdinalIgnoreCase)
            || exception is UnauthorizedAccessException;
    }

    /// <summary> Определяет, является ли исключение ошибкой отсутствия сущности. </summary>
    private static bool IsNotFoundError(Exception exception)
    {
        return exception.Message.Contains("не найден", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("not found", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary> Запрос на обновление статуса дефекта. </summary>
    public sealed class UpdateDefectStatusRequest
    {
        /// <summary> Новый код статуса дефекта. </summary>
        public string StatusCode { get; init; } = string.Empty;
    }
}
