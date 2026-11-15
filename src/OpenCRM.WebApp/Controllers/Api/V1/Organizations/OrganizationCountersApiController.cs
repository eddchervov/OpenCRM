using Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Vehicles.Contracts.Dtos;

namespace OpenCRM.WebApp.Controllers.Api.V1.Organizations;

/// <summary> API-контроллер показателей одометра автомобилей организации. </summary>
[ApiController]
[Authorize]
public class OrganizationCountersApiController(
    UserManager<ApplicationUser> userManager,
    IOrganizationSubscriptionService organizationSubscriptionService,
    IVehicleCounterQueryService vehicleService,
    IVehicleCounterCommandService counterCommands,
    ILogger<OrganizationCountersApiController> logger) : ControllerBase
{
    /// <summary> Возвращает постраничный список автомобилей с показателями одометра. </summary>
    [HttpGet("/api/v1/organizations/{organizationId:guid}/counters/vehicles/{page:int}/{pageSize:int}")]
    public async Task<ActionResult<VehicleCounterVehiclesResponse>> LoadVehicles(
        Guid organizationId,
        int page,
        int pageSize,
        Guid? organizationClientId = null,
        string? sort = null)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound();
        }

        if (!await organizationSubscriptionService.HasFeatureAsync(user.Id, organizationId, FeatureCodes.CountersModule))
        {
            return Forbid();
        }

        try
        {
            return Ok(await vehicleService.GetCounterVehiclesPageAsync(
                user.Id,
                organizationId,
                page: page,
                pageSize: pageSize,
                organizationClientId: organizationClientId,
                sort: sort));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to load counter vehicles for organization {OrganizationId} by user {UserId}", organizationId, user.Id);
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary> Возвращает постраничную историю показателей одометра. </summary>
    [HttpGet("/api/v1/organizations/{organizationId:guid}/counters/history/{page:int}/{pageSize:int}")]
    public async Task<ActionResult<VehicleCounterHistoryResponse>> LoadHistory(
        Guid organizationId,
        int page,
        int pageSize,
        Guid? vehicleId = null,
        Guid? organizationClientId = null,
        string? sort = null)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound();
        }

        if (!await organizationSubscriptionService.HasFeatureAsync(user.Id, organizationId, FeatureCodes.CountersModule))
        {
            return Forbid();
        }

        try
        {
            return Ok(await vehicleService.GetCounterHistoryPageAsync(
                user.Id,
                organizationId,
                page,
                pageSize,
                null,
                vehicleId,
                organizationClientId,
                sort));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to load counter history for organization {OrganizationId} by user {UserId}", organizationId, user.Id);
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary> Сохраняет запись показателя одометра автомобиля. </summary>
    [HttpPost("/api/v1/organizations/{organizationId:guid}/counters")]
    public async Task<ActionResult> Save(Guid organizationId, [FromBody] UpdateVehicleCounterDto dto)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound();
        }

        if (!await organizationSubscriptionService.HasFeatureAsync(user.Id, organizationId, FeatureCodes.CountersModule))
        {
            return Forbid();
        }

        try
        {
            await counterCommands.UpdateCounterAsync(user.Id, organizationId, dto);
            return Ok(new { message = "Показание сохранено." });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to save counter entry for organization {OrganizationId} by user {UserId}", organizationId, user.Id);
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary> Удаляет запись показателя одометра. </summary>
    [HttpDelete("/api/v1/organizations/{organizationId:guid}/counters/{entryId:guid}")]
    public async Task<ActionResult> Delete(Guid organizationId, Guid entryId)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound();
        }

        if (!await organizationSubscriptionService.HasFeatureAsync(user.Id, organizationId, FeatureCodes.CountersModule))
        {
            return Forbid();
        }

        try
        {
            await counterCommands.DeleteCounterEntryAsync(user.Id, organizationId, entryId);
            return Ok(new { message = "Запись удалена." });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to delete counter entry {EntryId} for organization {OrganizationId} by user {UserId}", entryId, organizationId, user.Id);
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary> Возвращает данные для графика показателей одометра автомобиля. </summary>
    [HttpGet("/api/v1/organizations/{organizationId:guid}/counters/chart")]
    public async Task<ActionResult<IReadOnlyList<VehicleCounterEntryDto>>> LoadChart(Guid organizationId, Guid vehicleId)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound();
        }

        if (!await organizationSubscriptionService.HasFeatureAsync(user.Id, organizationId, FeatureCodes.CountersModule))
        {
            return Forbid();
        }

        try
        {
            return Ok(await vehicleService.GetCounterTimelineAsync(user.Id, organizationId, vehicleId, 120));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to load counter chart for organization {OrganizationId}, vehicle {VehicleId} by user {UserId}", organizationId, vehicleId, user.Id);
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary> Возвращает показатель одометра автомобиля на момент времени. </summary>
    [HttpGet("/api/v1/organizations/{organizationId:guid}/counters/entry")]
    public async Task<ActionResult<VehicleCounterLookupDto>> LoadEntry(Guid organizationId, Guid vehicleId, DateTime recordedAt)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound();
        }

        if (!await organizationSubscriptionService.HasFeatureAsync(user.Id, organizationId, FeatureCodes.CountersModule))
        {
            return Forbid();
        }

        try
        {
            return Ok(await vehicleService.GetCounterEntryForDateAsync(user.Id, organizationId, vehicleId, recordedAt));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to load counter entry for organization {OrganizationId}, vehicle {VehicleId} by user {UserId}", organizationId, vehicleId, user.Id);
            return BadRequest(new { message = ex.Message });
        }
    }
}
