using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OpenCRM.Domain.Identity;
using OpenCRM.WebApp.Infrastructure.Security;
using OpenCRM.Modules.Communications.Contracts;
using OpenCRM.Modules.Communications.Contracts.Dtos;

namespace OpenCRM.WebApp.Controllers.Api.V1.Organizations;

/// <summary> Переписка организации, доступ сотрудников и личные отметки прочтения. </summary>
[ApiController]
[Authorize]
[Route("api/v1/organizations/{organizationId:guid}/conversations")]
public sealed class OrganizationConversationsApiController(
    UserManager<ApplicationUser> users,
    IOrganizationConversationService conversations) : ControllerBase
{
    private readonly UserManager<ApplicationUser> _users = users;
    private readonly IOrganizationConversationService _conversations = conversations;

    [HttpGet]
    public Task<IActionResult> Inbox(Guid organizationId, [FromQuery] ConversationInboxQueryDto query, CancellationToken cancellationToken) =>
        this.ExecuteAsync(userId => this._conversations.GetInboxAsync(userId, organizationId, query, cancellationToken));

    [HttpGet("client-inbox")]
    public Task<IActionResult> ClientInbox(Guid organizationId, [FromQuery] ConversationInboxQueryDto query, CancellationToken cancellationToken) =>
        this.ExecuteAsync(userId => this._conversations.GetClientInboxAsync(userId, organizationId, query, cancellationToken));

    [HttpGet("summary")]
    public Task<IActionResult> Summary(Guid organizationId, CancellationToken cancellationToken) =>
        this.ExecuteAsync(userId => this._conversations.GetUnreadSummaryAsync(userId, organizationId, cancellationToken));

    [HttpGet("delivery-health")]
    public Task<IActionResult> DeliveryHealth(Guid organizationId, CancellationToken cancellationToken) =>
        this.ExecuteAsync(userId => this._conversations.GetDeliveryHealthForOrganizationAsync(userId, organizationId, cancellationToken));

    [HttpGet("{conversationId:guid}/messages")]
    public Task<IActionResult> Messages(Guid organizationId, Guid conversationId, long? beforeSequence, int pageSize = 50, CancellationToken cancellationToken = default) =>
        this.ExecuteAsync(userId => this._conversations.GetMessagesAsync(userId, organizationId, conversationId, beforeSequence, pageSize, cancellationToken));

    [HttpPost("for-client/{clientId:guid}")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> ForClient(Guid organizationId, Guid clientId, CancellationToken cancellationToken) =>
        this.ExecuteAsync(userId => this._conversations.GetForClientAsync(userId, organizationId, clientId, cancellationToken), allowEmpty: true);

    [HttpPost("clients/{clientId:guid}/routes")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> ClientRoutes(Guid organizationId, Guid clientId, CancellationToken cancellationToken) =>
        this.ExecuteAsync(userId => this._conversations.GetClientRoutesAsync(userId, organizationId, clientId, cancellationToken), allowEmpty: true);

    [HttpPost("{conversationId:guid}/messages")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicyNames.ConversationSend)]
    public Task<IActionResult> Send(Guid organizationId, Guid conversationId, SendConversationMessageDto request, CancellationToken cancellationToken) =>
        this.ExecuteAsync(userId => this._conversations.SendAsync(userId, organizationId, conversationId, request, cancellationToken));

    [HttpPost("{conversationId:guid}/read")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Read(Guid organizationId, Guid conversationId, ReadConversationRequest request, CancellationToken cancellationToken) =>
        this.ExecuteAsync(userId => this._conversations.MarkReadAsync(userId, organizationId, conversationId, request.ThroughSequence, cancellationToken));

    [HttpPut("{conversationId:guid}/client")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> LinkClient(Guid organizationId, Guid conversationId, LinkConversationClientRequest request, CancellationToken cancellationToken) =>
        this.ExecuteAsync(userId => this._conversations.LinkClientAsync(userId, organizationId, conversationId, request.ClientId, cancellationToken));

    /// <summary> Отправляет изменения счётчика из БД; повторное соединение всегда получает актуальное состояние. </summary>
    [HttpGet("events")]
    [EnableRateLimiting(RateLimitPolicyNames.ConversationEvents)]
    public async Task Events(Guid organizationId, CancellationToken cancellationToken)
    {
        var userId = this._users.GetUserId(this.User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            this.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        ConversationUnreadSummaryDto summary;
        try
        {
            summary = await this._conversations.GetUnreadSummaryAsync(userId, organizationId, cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            this.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        this.Response.ContentType = "text/event-stream";
        this.Response.Headers.CacheControl = "no-store";
        this.Response.Headers["X-Accel-Buffering"] = "no";
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        string? revision = null;

        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
            do
            {
                if (revision != summary.Revision)
                {
                    await this.Response.WriteAsync($"event: summary\ndata: {JsonSerializer.Serialize(summary, jsonOptions)}\n\n", cancellationToken);
                    revision = summary.Revision;
                }
                else
                {
                    await this.Response.WriteAsync(": keep-alive\n\n", cancellationToken);
                }

                await this.Response.Body.FlushAsync(cancellationToken);
                if (!await timer.WaitForNextTickAsync(cancellationToken))
                {
                    break;
                }

                summary = await this._conversations.GetUnreadSummaryAsync(userId, organizationId, cancellationToken);
            }
            while (!cancellationToken.IsCancellationRequested);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Закрытие вкладки или смена организации завершает поток.
        }
        catch (UnauthorizedAccessException)
        {
            // Изменение членства или прав закрывает уже открытое соединение.
        }
    }

    private async Task<IActionResult> ExecuteAsync<T>(Func<string, Task<T>> action, bool allowEmpty = false)
    {
        var userId = this._users.GetUserId(this.User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return this.Unauthorized();
        }

        this.Response.Headers.CacheControl = "no-store";
        try
        {
            var result = await action(userId);

            return result is null ? allowEmpty ? this.NoContent() : this.NotFound() : this.Ok(result);
        }
        catch (UnauthorizedAccessException)
        {
            return this.Forbid();
        }
        catch (KeyNotFoundException)
        {
            return this.NotFound();
        }
        catch (ArgumentException exception)
        {
            return this.BadRequest(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return this.Conflict(new { message = exception.Message });
        }
    }

    public sealed record ReadConversationRequest(long ThroughSequence);

    public sealed record LinkConversationClientRequest(Guid? ClientId);
}
