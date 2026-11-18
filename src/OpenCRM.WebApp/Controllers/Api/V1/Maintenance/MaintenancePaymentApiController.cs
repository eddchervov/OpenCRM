using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.WebApp.Infrastructure.Localization;

namespace OpenCRM.WebApp.Controllers.Api.V1.Maintenance;

/// <summary> API-контроллер платежей по записи об обслуживании. </summary>
[ApiController]
[Authorize]
public class MaintenancePaymentApiController(
    IMaintenancePaymentService maintenancePaymentService,
    IStringLocalizer<LocalizationStrings> text) : MaintenanceApiControllerBase
{
    /// <summary> Возвращает список платежей по записи об обслуживании. </summary>
    [HttpGet("api/v1/maintenance/{maintenanceId:guid}/payments")]
    public async Task<IActionResult> GetPayments(Guid maintenanceId, [FromQuery] Guid organizationId)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null)
        {
            return userIdResult.Result;
        }
        var userId = userIdResult.Value!;
        try
        {
            return Ok(await maintenancePaymentService.GetPayments(userId, organizationId, maintenanceId));
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

    /// <summary> Добавляет платёж по записи об обслуживании. </summary>
    [HttpPost("api/v1/maintenance/{maintenanceId:guid}/payments")]
    public async Task<IActionResult> AddPayment(Guid maintenanceId, [FromQuery] Guid organizationId, [FromBody] AddMaintenancePaymentDto dto)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null)
        {
            return userIdResult.Result;
        }
        var userId = userIdResult.Value!;
        try
        {
            await maintenancePaymentService.AddPayment(userId, organizationId, maintenanceId, dto);
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
        catch (MaintenancePaymentValidationException ex)
        {
            var localizedMessage = text[ex.MessageKey, ex.Arguments.ToArray()];
            var message = localizedMessage.ResourceNotFound ? ex.Message : localizedMessage.Value;

            return BadRequest(new { message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary> Отменяет платёж по записи об обслуживании. </summary>
    [HttpPost("api/v1/maintenance/{maintenanceId:guid}/payments/{paymentId:guid}/cancel")]
    public async Task<IActionResult> CancelPayment(Guid maintenanceId, [FromQuery] Guid organizationId, Guid paymentId, [FromBody] CancelMaintenancePaymentDto dto)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null)
        {
            return userIdResult.Result;
        }
        var userId = userIdResult.Value!;
        try
        {
            await maintenancePaymentService.CancelPayment(userId, organizationId, maintenanceId, paymentId, dto.Reason);
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
}
