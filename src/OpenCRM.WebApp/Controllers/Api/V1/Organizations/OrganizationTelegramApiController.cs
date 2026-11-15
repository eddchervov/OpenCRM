using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Modules.Notifications.Telegram;

namespace OpenCRM.WebApp.Controllers.Api.V1.Organizations;

/// <summary> API настройки Telegram-бота организации. </summary>
[ApiController]
public sealed class OrganizationTelegramApiController(OrganizationTelegramService telegramService, IConfiguration configuration) : ControllerBase
{
    /// <summary> Запрос на сохранение настроек Telegram-бота организации. </summary>
    public sealed record SaveTelegramBotRequest(string? Token, bool IsEnabled, bool NotifyBooking, bool NotifyReady);

    /// <summary> Возвращает внутренние учётные данные Telegram-бота организации. </summary>
    [AllowAnonymous]
    [HttpGet("/api/internal/notification-bots/telegram/{organizationId:guid}")]
    public async Task<IActionResult> GetBotCredential(Guid organizationId, CancellationToken cancellationToken)
    {
        var configuredKey = configuration["TelegramOrganizationNotifications:ServiceKey"];
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
        var chatId = Request.Headers["X-Notification-Recipient"].ToString();
        var botToken = await telegramService.GetBotCredentialAsync(organizationId, notificationKey, chatId, cancellationToken);

        Response.Headers.CacheControl = "no-store";

        return botToken == null ? NotFound() : Ok(new { botToken });
    }

    /// <summary> Возвращает настройки Telegram-бота организации. </summary>
    [Authorize]
    [HttpGet("/api/v1/organizations/{organizationId:guid}/telegram-bot")]
    public async Task<IActionResult> GetSettings(Guid organizationId)
    {
        var userId = this.GetApiUserId();
        if (userId == null)
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await telegramService.GetSettingsAsync(userId, organizationId));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary> Сохраняет настройки Telegram-бота организации. </summary>
    [Authorize]
    [HttpPut("/api/v1/organizations/{organizationId:guid}/telegram-bot")]
    public async Task<IActionResult> SaveSettings(Guid organizationId, [FromBody] SaveTelegramBotRequest request, CancellationToken cancellationToken)
    {
        var userId = this.GetApiUserId();
        if (userId == null)
        {
            return Unauthorized();
        }

        try
        {
            await telegramService.SaveSettingsAsync(userId, organizationId, request.Token, request.IsEnabled,
                request.NotifyBooking, request.NotifyReady, cancellationToken);
            return Ok(await telegramService.GetSettingsAsync(userId, organizationId));
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

    /// <summary> Возвращает статус подключения клиента к Telegram-боту. </summary>
    [Authorize]
    [HttpGet("/api/v1/organizations/{organizationId:guid}/clients/{clientId:guid}/telegram")]
    public async Task<IActionResult> GetClientStatus(Guid organizationId, Guid clientId)
    {
        var userId = this.GetApiUserId();
        if (userId == null)
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await telegramService.GetClientStatusAsync(userId, organizationId, clientId));
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

    /// <summary> Создаёт ссылку для привязки клиента к Telegram-боту. </summary>
    [Authorize]
    [HttpPost("/api/v1/organizations/{organizationId:guid}/clients/{clientId:guid}/telegram/link")]
    public async Task<IActionResult> CreateClientLink(Guid organizationId, Guid clientId)
    {
        var userId = this.GetApiUserId();
        if (userId == null)
        {
            return Unauthorized();
        }

        try
        {
            return Ok(new { url = await telegramService.CreateClientLinkAsync(userId, organizationId, clientId) });
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

    /// <summary> Возвращает Telegram-уведомления по обслуживанию. </summary>
    [Authorize]
    [HttpGet("/api/v1/organizations/{organizationId:guid}/maintenances/{maintenanceId:guid}/telegram/notifications")]
    public async Task<IActionResult> GetMaintenanceNotifications(Guid organizationId, Guid maintenanceId)
    {
        var userId = this.GetApiUserId();
        if (userId == null)
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await telegramService.GetMaintenanceNotificationsAsync(userId, organizationId, maintenanceId));
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

    /// <summary> Отключает клиента от Telegram-бота организации. </summary>
    [Authorize]
    [HttpDelete("/api/v1/organizations/{organizationId:guid}/clients/{clientId:guid}/telegram")]
    public async Task<IActionResult> DisconnectClient(Guid organizationId, Guid clientId)
    {
        var userId = this.GetApiUserId();
        if (userId == null)
        {
            return Unauthorized();
        }

        try
        {
            await telegramService.DisconnectClientAsync(userId, organizationId, clientId);
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

    /// <summary> Принимает обновления вебхука Telegram-бота. </summary>
    [AllowAnonymous]
    [HttpPost("/api/internal/notification-bots/telegram/{organizationId:guid}/updates")]
    [RequestSizeLimit(64 * 1024)]
    public async Task<IActionResult> Webhook(Guid organizationId, [FromBody] JsonElement update)
    {
        var secret = Request.Headers["X-Telegram-Bot-Api-Secret-Token"].ToString();
        return await telegramService.ProcessWebhookAsync(organizationId, secret, update) ? Ok() : Unauthorized();
    }
}
