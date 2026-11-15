using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;

namespace OpenCRM.WebApp.Controllers.Api.V1.Maintenance;

/// <summary> Настройки и предпросмотр напоминаний о визите. </summary>
[ApiController]
[Authorize]
public sealed class MaintenanceVisitReminderApiController(IMaintenanceVisitReminderService service) : MaintenanceApiControllerBase
{
    public sealed record SetEnabledRequest(bool Enabled, uint Version);

    [HttpGet("api/v1/organizations/{organizationId:guid}/visit-reminder-settings")]
    public Task<IActionResult> GetSettings(Guid organizationId) =>
        this.ExecuteAsync(async userId => this.Ok(await service.GetSettingsAsync(userId, organizationId)));

    [HttpPut("api/v1/organizations/{organizationId:guid}/visit-reminder-settings")]
    public Task<IActionResult> SaveSettings(Guid organizationId, [FromBody] VisitReminderSettingsDto settings) =>
        this.ExecuteAsync(async userId =>
        {
            await service.SaveSettingsAsync(userId, organizationId, settings);

            return this.Ok(await service.GetSettingsAsync(userId, organizationId));
        });

    [HttpGet("api/v1/maintenance/{maintenanceId:guid}/visit-reminder")]
    public Task<IActionResult> GetReminder(Guid maintenanceId, [FromQuery] Guid organizationId) =>
        this.ExecuteAsync(async userId => this.Ok(await service.GetAsync(userId, organizationId, maintenanceId)));

    [HttpPut("api/v1/maintenance/{maintenanceId:guid}/visit-reminder")]
    public Task<IActionResult> SetEnabled(Guid maintenanceId, [FromQuery] Guid organizationId, [FromBody] SetEnabledRequest request) =>
        this.ExecuteAsync(async userId => this.Ok(await service.SetEnabledAsync(userId, organizationId, maintenanceId, request.Enabled, request.Version)));

    private async Task<IActionResult> ExecuteAsync(Func<string, Task<IActionResult>> action)
    {
        var userId = this.GetCurrentUserId();
        if (userId.Result != null)
        {
            return userId.Result;
        }

        try
        {
            return await action(userId.Value!);
        }
        catch (KeyNotFoundException)
        {
            return this.NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return this.Forbid();
        }
        catch (Exception exception) when (MaintenanceApiControllerBase.IsAccessDeniedError(exception))
        {
            return this.Forbid();
        }
        catch (DbUpdateConcurrencyException exception)
        {
            return this.Conflict(new { message = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return this.BadRequest(new { message = exception.Message });
        }
    }
}
