using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.WebApp.Infrastructure.Assistant.Telegram;

namespace OpenCRM.WebApp.Controllers.Api.V1;

/// <summary>Управляет подключением рабочего Telegram-помощника и принимает его вебхук.</summary>
[ApiController]
public sealed class AssistantTelegramController(AssistantTelegramService telegramService, IConfiguration configuration, IOptions<IdentityOptions> identityOptions) : ControllerBase
{
    private readonly AssistantTelegramConfiguration _settings = new(configuration);

    [Authorize]
    [HttpGet("/api/v1/assistant/telegram/status")]
    public async Task<IActionResult> GetStatus([FromQuery] Guid organizationId, CancellationToken cancellationToken)
    {
        var userId = this.User.FindFirstValue(identityOptions.Value.ClaimsIdentity.UserIdClaimType);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await telegramService.GetStatusAsync(userId, organizationId, cancellationToken));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [Authorize]
    [HttpGet("/api/v1/assistant/telegram/connections")]
    public async Task<IActionResult> GetConnections(
        [FromQuery] Guid organizationId,
        [FromQuery] int page = 1,
        CancellationToken cancellationToken = default)
    {
        var userId = this.User.FindFirstValue(identityOptions.Value.ClaimsIdentity.UserIdClaimType);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await telegramService.GetOrganizationConnectionsAsync(userId, organizationId, page, cancellationToken));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [Authorize]
    [HttpPost("/api/v1/assistant/telegram/link")]
    public async Task<IActionResult> CreateLink([FromBody] CreateLinkRequest request, CancellationToken cancellationToken)
    {
        var userId = this.User.FindFirstValue(identityOptions.Value.ClaimsIdentity.UserIdClaimType);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        try
        {
            var result = await telegramService.CreateLinkAsync(userId, request.OrganizationId, cancellationToken);
            return Ok(new { url = result.Url, expiresAt = result.ExpiresAt });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [Authorize]
    [HttpDelete("/api/v1/assistant/telegram/binding")]
    public async Task<IActionResult> Disconnect([FromQuery] Guid organizationId, CancellationToken cancellationToken)
    {
        var userId = this.User.FindFirstValue(identityOptions.Value.ClaimsIdentity.UserIdClaimType);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        try
        {
            await telegramService.DisconnectAsync(userId, organizationId, cancellationToken);
            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [Authorize]
    [HttpPut("/api/v1/assistant/telegram/speech")]
    public async Task<IActionResult> SetSpeechReplies([FromQuery] Guid organizationId, [FromBody] SpeechRepliesRequest request, CancellationToken cancellationToken)
    {
        var userId = this.User.FindFirstValue(identityOptions.Value.ClaimsIdentity.UserIdClaimType);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        try
        {
            await telegramService.SetSpeechRepliesAsync(userId, organizationId, request.Enabled, cancellationToken);
            return Ok();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [AllowAnonymous]
    [HttpPost("/api/internal/assistant/telegram/webhook")]
    public async Task<IActionResult> Webhook(CancellationToken cancellationToken)
    {
        if (this._settings.LongPollingEnabled)
        {
            return NotFound();
        }

        if (!this.Request.IsHttps)
        {
            return BadRequest();
        }

        var suppliedSecret = this.Request.Headers["X-Telegram-Bot-Api-Secret-Token"].ToString();
        if (!this._settings.IsConfigured || !this._settings.MatchesWebhookSecret(suppliedSecret))
        {
            return Unauthorized();
        }

        var payload = await this.ReadLimitedWebhookBodyAsync(this._settings.MaximumPayloadBytes, cancellationToken);
        if (payload is null)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payload);
        }
        catch (JsonException)
        {
            return BadRequest();
        }

        using (document)
        {
            var accepted = await telegramService.EnqueueWebhookAsync(document.RootElement, suppliedSecret, cancellationToken);

            return accepted ? Ok(new { accepted = true }) : Unauthorized();
        }
    }

    private async Task<byte[]?> ReadLimitedWebhookBodyAsync(int maximumBytes, CancellationToken cancellationToken)
    {
        if (this.Request.ContentLength > maximumBytes)
        {
            return null;
        }

        await using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var read = await this.Request.Body.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                return buffer.ToArray();
            }
            if (buffer.Length + read > maximumBytes)
            {
                return null;
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
    }

    public sealed record CreateLinkRequest(Guid OrganizationId);
    public sealed record SpeechRepliesRequest(bool Enabled);
}
