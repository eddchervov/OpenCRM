using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace OpenCRM.WebApp.Controllers.Api.V1;

/// <summary> API выдачи antiforgery-токенов. </summary>
[Route("api/v1/antiforgery")]
[ApiController]
[Authorize]
public sealed class AntiforgeryApiController(IAntiforgery antiforgery) : ControllerBase
{
    /// <summary> Возвращает antiforgery-токен. </summary>
    [HttpGet("token")]
    public IActionResult GetToken()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);

        return Ok(new { requestToken = tokens.RequestToken });
    }
}
