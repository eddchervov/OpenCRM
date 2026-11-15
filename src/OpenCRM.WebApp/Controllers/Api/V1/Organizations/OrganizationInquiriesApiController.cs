using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Clients.Contracts;
using OpenCRM.Modules.Clients.Contracts.Dtos;

namespace OpenCRM.WebApp.Controllers.Api.V1.Organizations;

/// <summary> API управления ручными обращениями организации. </summary>
[ApiController]
[Authorize]
public sealed class OrganizationInquiriesApiController(
    UserManager<ApplicationUser> userManager,
    IOrganizationInquiryService inquiries,
    ILogger<OrganizationInquiriesApiController> logger) : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager = userManager;
    private readonly IOrganizationInquiryService _inquiries = inquiries;
    private readonly ILogger<OrganizationInquiriesApiController> _logger = logger;

    [HttpGet("/api/v1/organizations/{organizationId:guid}/inquiries/action-reminders")]
    public async Task<IActionResult> ActionReminders(Guid organizationId, CancellationToken cancellationToken)
    {
        var userId = this._userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }
        return await this.ExecuteAsync(() => this._inquiries.GetActionRemindersAsync(userId, organizationId, cancellationToken));
    }

    [HttpGet("/api/v1/organizations/{organizationId:guid}/inquiries")]
    public async Task<IActionResult> Page(Guid organizationId, [FromQuery] OrganizationInquiryQueryDto query, CancellationToken cancellationToken)
    {
        var userId = this._userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }
        return await this.ExecuteAsync(() => this._inquiries.GetPageAsync(userId, organizationId, query, cancellationToken));
    }

    [HttpGet("/api/v1/organizations/{organizationId:guid}/inquiries/board")]
    public async Task<IActionResult> Board(Guid organizationId, [FromQuery] OrganizationInquiryQueryDto query, int pageSize = 10, CancellationToken cancellationToken = default)
    {
        var userId = this._userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }
        return await this.ExecuteAsync(() => this._inquiries.GetBoardAsync(userId, organizationId, query, pageSize, cancellationToken));
    }

    [HttpPost("/api/v1/organizations/{organizationId:guid}/inquiries")]
    public async Task<IActionResult> Create(Guid organizationId, [FromBody] OrganizationInquirySaveDto request, CancellationToken cancellationToken)
    {
        var userId = this._userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }
        var result = await this.ExecuteAsync(() => this._inquiries.CreateAsync(userId, organizationId, request, cancellationToken));
        return result is OkObjectResult { Value: OrganizationInquiryDto dto }
            ? Created($"/api/v1/organizations/{organizationId}/inquiries/{dto.Id}", dto)
            : result;
    }

    [HttpGet("/api/v1/organizations/{organizationId:guid}/inquiries/{inquiryId:guid}")]
    public async Task<IActionResult> Details(Guid organizationId, Guid inquiryId, CancellationToken cancellationToken)
    {
        var userId = this._userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }
        return await this.ExecuteAsync(async () => (object?)await this._inquiries.GetAsync(userId, organizationId, inquiryId, cancellationToken));
    }

    [HttpPut("/api/v1/organizations/{organizationId:guid}/inquiries/{inquiryId:guid}")]
    public async Task<IActionResult> Update(Guid organizationId, Guid inquiryId, [FromBody] OrganizationInquirySaveDto request, CancellationToken cancellationToken)
    {
        var userId = this._userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }
        return await this.ExecuteAsync(() => this._inquiries.UpdateAsync(userId, organizationId, inquiryId, request, cancellationToken));
    }

    [HttpGet("/api/v1/organizations/{organizationId:guid}/inquiries/{inquiryId:guid}/history")]
    public async Task<IActionResult> History(Guid organizationId, Guid inquiryId, int page = 1, int pageSize = 30, CancellationToken cancellationToken = default)
    {
        var userId = this._userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }
        return await this.ExecuteAsync(() => this._inquiries.GetHistoryAsync(userId, organizationId, inquiryId, page, pageSize, cancellationToken));
    }

    [HttpPost("/api/v1/organizations/{organizationId:guid}/inquiries/{inquiryId:guid}/stage")]
    public async Task<IActionResult> ChangeStage(Guid organizationId, Guid inquiryId, [FromBody] ChangeInquiryStageRequest request, CancellationToken cancellationToken)
    {
        var userId = this._userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }
        return await this.ExecuteAsync(() => this._inquiries.ChangeStageAsync(userId, organizationId, inquiryId, request.StageCode, request.Version, request.Comment, cancellationToken));
    }

    [HttpPost("/api/v1/organizations/{organizationId:guid}/inquiries/{inquiryId:guid}/action/complete")]
    public async Task<IActionResult> CompleteAction(Guid organizationId, Guid inquiryId, [FromBody] InquiryVersionRequest request, CancellationToken cancellationToken)
    {
        var userId = this._userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }
        return await this.ExecuteAsync(() => this._inquiries.CompleteActionAsync(userId, organizationId, inquiryId, request.Version, cancellationToken));
    }

    [HttpPost("/api/v1/organizations/{organizationId:guid}/inquiries/{inquiryId:guid}/action/reschedule")]
    public async Task<IActionResult> RescheduleAction(Guid organizationId, Guid inquiryId, [FromBody] RescheduleInquiryActionRequest request, CancellationToken cancellationToken)
    {
        var userId = this._userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }
        return await this.ExecuteAsync(() => this._inquiries.RescheduleActionAsync(
            userId,
            organizationId,
            inquiryId,
            request.NextActionAt,
            request.Version,
            cancellationToken));
    }

    [HttpPost("/api/v1/organizations/{organizationId:guid}/inquiries/{inquiryId:guid}/close")]
    public async Task<IActionResult> Close(Guid organizationId, Guid inquiryId, [FromBody] CloseInquiryRequest request, CancellationToken cancellationToken)
    {
        var userId = this._userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }
        return await this.ExecuteAsync(() => this._inquiries.CloseAsync(userId, organizationId, inquiryId, request.Reason, request.Version, cancellationToken));
    }

    [HttpPost("/api/v1/organizations/{organizationId:guid}/inquiries/{inquiryId:guid}/notes")]
    public async Task<IActionResult> AddNote(Guid organizationId, Guid inquiryId, [FromBody] AddInquiryNoteRequest request, CancellationToken cancellationToken)
    {
        var userId = this._userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }
        return await this.ExecuteAsync(() => this._inquiries.AddNoteAsync(userId, organizationId, inquiryId, request.Note, request.Version, cancellationToken));
    }

    private async Task<IActionResult> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try
        {
            var result = await action();
            return result is null ? NotFound() : Ok(result);
        }
        catch (InquiryConflictException exception)
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
            this._logger.LogError(exception, "Ошибка обращения к ручным обращениям организации");
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Не удалось выполнить операцию с обращением." });
        }
    }

    public sealed class ChangeInquiryStageRequest
    {
        public string? Comment { get; init; }
        public string StageCode { get; init; } = string.Empty;
        public long Version { get; init; }
    }

    public class InquiryVersionRequest
    {
        public long Version { get; init; }
    }

    public sealed class RescheduleInquiryActionRequest : InquiryVersionRequest
    {
        public DateTime NextActionAt { get; init; }
    }

    public sealed class CloseInquiryRequest : InquiryVersionRequest
    {
        public string Reason { get; init; } = string.Empty;
    }

    public sealed class AddInquiryNoteRequest : InquiryVersionRequest
    {
        public string Note { get; init; } = string.Empty;
    }
}
