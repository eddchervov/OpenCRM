using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;

namespace OpenCRM.WebApp.Controllers.Api.V1.Maintenance;

/// <summary> Отметки повреждений в приёмке заказа. </summary>
[ApiController]
[Authorize]
[Route("api/v1/maintenance/{maintenanceId:guid}/intake/damages")]
public sealed class MaintenanceIntakeDamageApiController(
    IMaintenanceIntakeDamageService service,
    ILogger<MaintenanceIntakeDamageApiController> logger) : MaintenanceApiControllerBase
{
    [HttpGet]
    public Task<IActionResult> GetDamages(Guid maintenanceId, [FromQuery] Guid organizationId) =>
        this.ExecuteAsync(organizationId, maintenanceId, null,
            async userId => this.Ok(await service.GetDamages(userId, organizationId, maintenanceId)));

    [HttpPut("{damageId:guid}")]
    public Task<IActionResult> SaveDamage(
        Guid maintenanceId, Guid damageId, [FromQuery] Guid organizationId, [FromBody] SaveMaintenanceIntakeDamageDto dto) =>
        this.ExecuteAsync(organizationId, maintenanceId, damageId, async userId =>
        {
            await service.SaveDamage(userId, organizationId, maintenanceId, damageId, dto);

            return this.NoContent();
        });

    [HttpDelete("{damageId:guid}")]
    public Task<IActionResult> DeleteDamage(Guid maintenanceId, Guid damageId, [FromQuery] Guid organizationId, [FromQuery] uint version) =>
        this.ExecuteAsync(organizationId, maintenanceId, damageId, async userId =>
        {
            await service.DeleteDamage(userId, organizationId, maintenanceId, damageId, version);

            return this.NoContent();
        });

    private async Task<IActionResult> ExecuteAsync(
        Guid organizationId,
        Guid maintenanceId,
        Guid? damageId,
        Func<string, Task<IActionResult>> action)
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
        catch (Exception exception) when (MaintenanceApiControllerBase.IsAccessDeniedError(exception))
        {
            return this.Forbid();
        }
        catch (Exception exception) when (MaintenanceApiControllerBase.IsNotFoundError(exception))
        {
            return this.NotFound();
        }
        catch (DbUpdateConcurrencyException exception)
        {
            logger.LogWarning(exception,
                "Конфликт приёмки при {Operation}: организация {OrganizationId}, заказ {MaintenanceId}, "
                + "повреждение {DamageId}, пользователь {UserId}, ожидаемая версия {ExpectedVersion}, текущая версия {ActualVersion}, TraceId: {TraceId}.",
                this.Request.Method, organizationId, maintenanceId, damageId, userId.Value,
                exception.Data["ExpectedVersion"], exception.Data["ActualVersion"], this.HttpContext.TraceIdentifier);

            return this.Conflict(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return this.BadRequest(new { message = exception.Message });
        }
    }
}
