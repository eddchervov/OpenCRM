using Microsoft.AspNetCore.Authorization;
using OpenCRM.WebApp.Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Services.Interfaces.Payments;
using System.Text.Json;
using OpenCRM.Services.Interfaces.Payments.Dto;

namespace OpenCRM.WebApp.Controllers.Payments;

/// <summary> API-контроллер приёма вебхуков ЮKassa. </summary>
[Route("api/webhook/yookassa")]
[ApiController]
[CommercialModule]
public class YooKassaWebhookController : ControllerBase
{
    /// <summary> Сервис обработки вебхуков ЮKassa. </summary>
    private readonly IYooKassaWebhookService _webhookService;

    /// <summary> Сервис работы с API ЮKassa. </summary>
    private readonly IYooKassaService _yooKassaService;

    /// <summary> Журнал логирования для контроллера. </summary>
    private readonly ILogger<YooKassaWebhookController> _logger;

    /// <summary> Создаёт контроллер вебхуков с его зависимостями. </summary>
    public YooKassaWebhookController(
        IYooKassaWebhookService webhookService,
        IYooKassaService yooKassaService,
        ILogger<YooKassaWebhookController> logger)
    {
        this._webhookService = webhookService;
        _yooKassaService = yooKassaService;
        _logger = logger;
    }

    /// <summary> Принимает и обрабатывает уведомление от ЮKassa. </summary>
    [HttpPost]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> HandleWebhook()
    {
        try
        {
            using var reader = new StreamReader(Request.Body);
            var json = await reader.ReadToEndAsync();

            var isValid = await _yooKassaService.VerifyNotification(json, string.Empty);

            if (!isValid)
            {
                _logger.LogWarning("Invalid signature for YooKassa webhook");
                return Unauthorized();
            }

            var notification = JsonSerializer.Deserialize<YooKassaNotification>(json);
            if (notification == null)
            {
                _logger.LogError("Failed to deserialize YooKassa notification");
                return BadRequest();
            }

            await this._webhookService.HandleAsync(notification);

            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing YooKassa webhook");
            return StatusCode(500);
        }
    }
}
