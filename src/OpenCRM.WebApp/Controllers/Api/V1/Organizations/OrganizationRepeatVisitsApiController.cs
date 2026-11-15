using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Clients.Contracts;
using OpenCRM.Modules.Clients.Contracts.Dtos;

namespace OpenCRM.WebApp.Controllers.Api.V1.Organizations;

/// <summary> API необязательных рекомендаций повторных визитов организации. </summary>
[ApiController]
[Authorize]
public sealed class OrganizationRepeatVisitsApiController(
    UserManager<ApplicationUser> userManager,
    IOrganizationRepeatVisitService repeatVisits,
    ILogger<OrganizationRepeatVisitsApiController> logger) : ControllerBase
{
    [HttpGet("/api/v1/organizations/{organizationId:guid}/repeat-visits/options")]
    public Task<IActionResult> Options(Guid organizationId, CancellationToken cancellationToken) =>
        ExecuteAsync(userId => repeatVisits.GetOptionsAsync(userId, organizationId, cancellationToken));

    [HttpGet("/api/v1/organizations/{organizationId:guid}/repeat-visits")]
    public Task<IActionResult> Page(Guid organizationId, [FromQuery] OrganizationRepeatVisitQueryDto query, CancellationToken cancellationToken) =>
        ExecuteAsync(userId => repeatVisits.GetPageAsync(userId, organizationId, query, cancellationToken));

    [HttpPost("/api/v1/organizations/{organizationId:guid}/repeat-visits")]
    public async Task<IActionResult> Create(Guid organizationId, [FromBody] OrganizationRepeatVisitSaveDto request, CancellationToken cancellationToken)
    {
        var result = await ExecuteAsync(userId => repeatVisits.CreateAsync(userId, organizationId, request, cancellationToken));
        return result is OkObjectResult { Value: OrganizationRepeatVisitDto dto }
            ? Created($"/api/v1/organizations/{organizationId}/repeat-visits/{dto.Id}", dto)
            : result;
    }

    [HttpGet("/api/v1/organizations/{organizationId:guid}/repeat-visits/{repeatVisitId:guid}")]
    public Task<IActionResult> Details(Guid organizationId, Guid repeatVisitId, CancellationToken cancellationToken) =>
        ExecuteAsync(userId => repeatVisits.GetAsync(userId, organizationId, repeatVisitId, cancellationToken));

    [HttpPut("/api/v1/organizations/{organizationId:guid}/repeat-visits/{repeatVisitId:guid}")]
    public Task<IActionResult> Update(Guid organizationId, Guid repeatVisitId, [FromBody] OrganizationRepeatVisitSaveDto request, CancellationToken cancellationToken) =>
        ExecuteAsync(userId => repeatVisits.UpdateAsync(userId, organizationId, repeatVisitId, request, cancellationToken));

    [HttpPost("/api/v1/organizations/{organizationId:guid}/repeat-visits/{repeatVisitId:guid}/action/complete")]
    public Task<IActionResult> CompleteAction(Guid organizationId, Guid repeatVisitId, [FromBody] OrganizationRepeatVisitVersionDto request, CancellationToken cancellationToken) =>
        ExecuteAsync(userId => repeatVisits.CompleteActionAsync(userId, organizationId, repeatVisitId, request.Version, cancellationToken));

    [HttpPost("/api/v1/organizations/{organizationId:guid}/repeat-visits/{repeatVisitId:guid}/action/reschedule")]
    public Task<IActionResult> RescheduleAction(Guid organizationId, Guid repeatVisitId, [FromBody] OrganizationRepeatVisitRescheduleDto request, CancellationToken cancellationToken) =>
        ExecuteAsync(userId => repeatVisits.RescheduleActionAsync(userId, organizationId, repeatVisitId, request.ContactByUtc, request.Version, cancellationToken));

    [HttpPost("/api/v1/organizations/{organizationId:guid}/repeat-visits/{repeatVisitId:guid}/state")]
    public Task<IActionResult> ChangeState(Guid organizationId, Guid repeatVisitId, [FromBody] OrganizationRepeatVisitStateDto request, CancellationToken cancellationToken) =>
        ExecuteAsync(userId => repeatVisits.ChangeStateAsync(userId, organizationId, repeatVisitId, request.IsActive, request.Version, cancellationToken));

    private async Task<IActionResult> ExecuteAsync<T>(Func<string, Task<T>> action)
    {
        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }
        try
        {
            var result = await action(userId);
            return result is null ? NotFound() : Ok(result);
        }
        catch (RepeatVisitConflictException exception)
        {
            return Conflict(new { message = exception.Message });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Ошибка работы с рекомендациями повторных визитов организации");
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Не удалось выполнить операцию с рекомендацией повторного визита." });
        }
    }
}
