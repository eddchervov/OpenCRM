using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.WebApp.Controllers.Api.V1.Organizations;

/// <summary> API управления сменами сотрудников организации. </summary>
[ApiController]
[Authorize]
[AutoValidateAntiforgeryToken]
[Route("api/v1/organizations/{organizationId:guid}/shifts")]
public sealed class OrganizationShiftsApiController(IOrganizationShiftService shiftService) : ControllerBase
{
    /// <summary> Возвращает смены организации за указанную неделю. </summary>
    [HttpGet]
    public async Task<IActionResult> GetWeek(Guid organizationId, [FromQuery] DateOnly weekStart)
    {
        return await ExecuteAsync(() => shiftService.GetWeekAsync(this.GetRequiredUserId(), organizationId, weekStart));
    }

    /// <summary> Возвращает смены текущего сотрудника. </summary>
    [HttpGet("mine")]
    public async Task<IActionResult> GetOwn(Guid organizationId)
    {
        return await ExecuteAsync(() => shiftService.GetOwnAsync(this.GetRequiredUserId(), organizationId));
    }

    /// <summary> Возвращает доступность сотрудника за период. </summary>
    [HttpGet("availability")]
    public async Task<IActionResult> GetAvailability(
        Guid organizationId,
        [FromQuery] Guid employeeId,
        [FromQuery] string startLocal,
        [FromQuery] string endLocal)
    {
        return await ExecuteAsync(() => shiftService.GetAvailabilityAsync(
            this.GetRequiredUserId(), organizationId, employeeId, startLocal, endLocal));
    }

    /// <summary> Сохраняет часовой пояс графика смен организации. </summary>
    [HttpPut("settings")]
    public async Task<IActionResult> SetTimeZone(Guid organizationId, [FromBody] ShiftTimeZoneRequest request)
    {
        return await ExecuteAsync(async () =>
        {
            await shiftService.SetTimeZoneAsync(this.GetRequiredUserId(), organizationId, request.TimeZoneId);
            return new { saved = true };
        });
    }

    /// <summary> Создаёт или обновляет смену организации. </summary>
    [HttpPost]
    [HttpPut("{shiftId:guid}")]
    public async Task<IActionResult> Save(Guid organizationId, [FromRoute] Guid? shiftId, [FromBody] SaveOrganizationShiftDto dto)
    {
        return await ExecuteAsync(async () => new { id = await shiftService.SaveAsync(this.GetRequiredUserId(), organizationId, shiftId, dto) });
    }

    /// <summary> Отменяет смену организации с указанием причины. </summary>
    [HttpDelete("{shiftId:guid}")]
    public async Task<IActionResult> Cancel(Guid organizationId, Guid shiftId, [FromBody] CancelShiftRequest request)
    {
        return await ExecuteAsync(async () =>
        {
            await shiftService.CancelAsync(this.GetRequiredUserId(), organizationId, shiftId, request.Version, request.Reason);
            return new { cancelled = true };
        });
    }

    /// <summary> Публикует смены на указанную неделю. </summary>
    [HttpPost("publish")]
    public async Task<IActionResult> Publish(Guid organizationId, [FromBody] PublishShiftWeekRequest request)
    {
        return await ExecuteAsync(async () => new
        {
            count = await shiftService.PublishWeekAsync(this.GetRequiredUserId(), organizationId, request.WeekStart)
        });
    }

    /// <summary> Копирует смены предыдущей недели на новую. </summary>
    [HttpPost("copy-previous-week")]
    public async Task<IActionResult> CopyPreviousWeek(Guid organizationId, [FromBody] PublishShiftWeekRequest request)
    {
        return await ExecuteAsync(async () => new
        {
            count = await shiftService.CopyPreviousWeekAsync(this.GetRequiredUserId(), organizationId, request.WeekStart)
        });
    }

    /// <summary> Возвращает идентификатор текущего пользователя. </summary>
    private string GetRequiredUserId() => this.GetApiUserId() ?? throw new UnauthorizedAccessException();

    /// <summary> Выполняет действие и оборачивает результат или ошибку в ответ контроллера. </summary>
    private async Task<IActionResult> ExecuteAsync<T>(Func<Task<T>> action)
    {
        if (string.IsNullOrWhiteSpace(this.GetApiUserId()))
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await action());
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (DbUpdateConcurrencyException exception)
        {
            return Conflict(new { message = exception.Message });
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    /// <summary> Запрос на установку часового пояса смен. </summary>
    public sealed record ShiftTimeZoneRequest(string TimeZoneId);

    /// <summary> Запрос на отмену смены с версией и причиной. </summary>
    public sealed record CancelShiftRequest(uint Version, string? Reason);

    /// <summary> Запрос на публикацию смен за неделю. </summary>
    public sealed record PublishShiftWeekRequest(DateOnly WeekStart);
}
