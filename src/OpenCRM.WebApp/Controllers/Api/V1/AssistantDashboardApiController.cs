using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using OpenCRM.WebApp.Infrastructure.Assistant;

namespace OpenCRM.WebApp.Controllers.Api.V1;

/// <summary>Показывает историю и расходы помощника в доступной пользователю организации.</summary>
[ApiController]
[Authorize]
[EnableRateLimiting("assistant-dashboard")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/v1/assistant")]
public sealed class AssistantDashboardApiController(
    AssistantDashboardService dashboard,
    IOptions<IdentityOptions> identityOptions) : ControllerBase
{
    [HttpGet("history")]
    public async Task<IActionResult> History(Guid organizationId, string? channel, int page = 1, CancellationToken cancellationToken = default)
    {
        return this.Ok(await dashboard.GetHistoryAsync(this.Actor(organizationId), channel, page, cancellationToken));
    }

    [HttpGet("history/{id:guid}")]
    public async Task<IActionResult> HistoryConversation(Guid id, Guid organizationId, int page = 1, CancellationToken cancellationToken = default)
    {
        var result = await dashboard.GetConversationAsync(this.Actor(organizationId), id, page, cancellationToken);

        return result is null ? this.NotFound() : this.Ok(result);
    }

    [HttpGet("usage")]
    public async Task<IActionResult> Usage(Guid organizationId, DateOnly? from, DateOnly? to, string? channel, CancellationToken cancellationToken)
    {
        return this.Ok(await dashboard.GetUsageAsync(this.Actor(organizationId), from, to, channel, cancellationToken));
    }

    private AssistantActor Actor(Guid organizationId) => new(
        this.User.FindFirstValue(identityOptions.Value.ClaimsIdentity.UserIdClaimType)
            ?? throw new UnauthorizedAccessException("Сессия завершилась."), organizationId, "web");
}
