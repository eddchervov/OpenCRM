using OpenCRM.WebApp.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Services.Interfaces.Payments;

namespace OpenCRM.WebApp.Controllers.Payments;

/// <summary> API-контроллер скачивания чеков НПД по токену. </summary>
[ApiController]
[Route("api/npd-receipts/download")]
[CommercialModule]
public sealed class NpdReceiptDownloadController(INpdReceiptDeliveryService deliveryService) : ControllerBase
{
    /// <summary> Возвращает файл чека НПД по токену. </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Download([FromQuery] string token)
    {
        var file = await deliveryService.GetFileAsync(token);
        if (file == null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "private, no-store";

        return File(file.Content, file.ContentType, file.FileName);
    }
}
