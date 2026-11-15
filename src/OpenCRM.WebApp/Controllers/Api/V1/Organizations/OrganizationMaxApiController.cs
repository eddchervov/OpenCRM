using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Modules.Notifications.Max;

namespace OpenCRM.WebApp.Controllers.Api.V1.Organizations;

/// <summary> API-контроллер настройки бота Max для организации. </summary>
[ApiController]
public sealed class OrganizationMaxApiController(OrganizationMaxService maxService, IConfiguration configuration) : ControllerBase
{
    /// <summary> Запрос на сохранение настроек бота Max организации. </summary>
    public sealed record SaveMaxBotRequest(string? Token, bool IsEnabled, bool NotifyBooking, bool NotifyReady);

    /// <summary> Возвращает внутренние учётные данные бота Max организации. </summary>
    [AllowAnonymous]
    [HttpGet("/api/internal/notification-bots/max/{organizationId:guid}")]
    public async Task<IActionResult> GetBotCredential(Guid organizationId, CancellationToken cancellationToken)
    {
        var configuredKey = configuration["MaxNotifications:ServiceKey"];
        var suppliedKey = Request.Headers["X-Notification-Service-Key"].ToString();
        if (!Request.IsHttps || string.IsNullOrWhiteSpace(configuredKey)
            || configuredKey.Length < 32 || string.IsNullOrWhiteSpace(suppliedKey))
        {
            return Unauthorized();
        }

        var configuredBytes = Encoding.UTF8.GetBytes(configuredKey);
        var suppliedBytes = Encoding.UTF8.GetBytes(suppliedKey);
        if (!CryptographicOperations.FixedTimeEquals(configuredBytes, suppliedBytes))
        {
            return Unauthorized();
        }

        var notificationKey = Request.Headers["X-Notification-Key"].ToString();
        var botToken = await maxService.GetBotCredentialAsync(organizationId, notificationKey, cancellationToken);

        Response.Headers.CacheControl = "no-store";

        return botToken == null ? NotFound() : Ok(new { botToken });
    }

    /// <summary> Возвращает настройки бота Max организации. </summary>
    [Authorize]
    [HttpGet("/api/v1/organizations/{organizationId:guid}/max-bot")]
    public async Task<IActionResult> GetSettings(Guid organizationId)
    {
        var userId = this.GetApiUserId();
        if (userId == null)
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await maxService.GetSettingsAsync(userId, organizationId));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary> Сохраняет настройки бота Max организации. </summary>
    [Authorize]
    [HttpPut("/api/v1/organizations/{organizationId:guid}/max-bot")]
    public async Task<IActionResult> SaveSettings(Guid organizationId, [FromBody] SaveMaxBotRequest request, CancellationToken cancellationToken)
    {
        var userId = this.GetApiUserId();
        if (userId == null)
        {
            return Unauthorized();
        }

        try
        {
            await maxService.SaveSettingsAsync(userId, organizationId, request.Token, request.IsEnabled,
                request.NotifyBooking, request.NotifyReady, cancellationToken);
            return Ok(await maxService.GetSettingsAsync(userId, organizationId));
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

    /// <summary> Возвращает статус подключения клиента к боту Max. </summary>
    [Authorize]
    [HttpGet("/api/v1/organizations/{organizationId:guid}/clients/{clientId:guid}/max")]
    public async Task<IActionResult> GetClientStatus(Guid organizationId, Guid clientId)
    {
        var userId = this.GetApiUserId();
        if (userId == null)
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await maxService.GetClientStatusAsync(userId, organizationId, clientId));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary> Создаёт ссылку для привязки клиента к боту Max. </summary>
    [Authorize]
    [HttpPost("/api/v1/organizations/{organizationId:guid}/clients/{clientId:guid}/max/link")]
    public async Task<IActionResult> CreateClientLink(Guid organizationId, Guid clientId)
    {
        var userId = this.GetApiUserId();
        if (userId == null)
        {
            return Unauthorized();
        }

        try
        {
            return Ok(new { url = await maxService.CreateClientLinkAsync(userId, organizationId, clientId) });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    /// <summary> Возвращает уведомления бота Max по обслуживанию. </summary>
    [Authorize]
    [HttpGet("/api/v1/organizations/{organizationId:guid}/maintenances/{maintenanceId:guid}/max/notifications")]
    public async Task<IActionResult> GetMaintenanceNotifications(Guid organizationId, Guid maintenanceId)
    {
        var userId = this.GetApiUserId();
        if (userId == null)
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await maxService.GetMaintenanceNotificationsAsync(userId, organizationId, maintenanceId));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary> Отключает клиента от бота Max организации. </summary>
    [Authorize]
    [HttpDelete("/api/v1/organizations/{organizationId:guid}/clients/{clientId:guid}/max")]
    public async Task<IActionResult> DisconnectClient(Guid organizationId, Guid clientId)
    {
        var userId = this.GetApiUserId();
        if (userId == null)
        {
            return Unauthorized();
        }

        try
        {
            await maxService.DisconnectClientAsync(userId, organizationId, clientId);
            return NoContent();
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary> Принимает обновления вебхука бота Max. </summary>
    [AllowAnonymous]
    [HttpPost("/api/webhooks/max/{organizationId:guid}")]
    [RequestSizeLimit(64 * 1024)]
    public async Task<IActionResult> Webhook(Guid organizationId, [FromBody] JsonElement update)
    {
        var secret = Request.Headers["X-Max-Bot-Api-Secret"].ToString();
        return await maxService.ProcessWebhookAsync(organizationId, secret, update) ? Ok() : Unauthorized();
    }
}
