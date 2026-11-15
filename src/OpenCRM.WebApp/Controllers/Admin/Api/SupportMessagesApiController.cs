using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Admin.Dtos;
using OpenCRM.Services.Interfaces.Auth;
using OpenCRM.Services.Interfaces.Home;

namespace OpenCRM.WebApp.Controllers.Admin.Api;

/// <summary> API управления обращениями в поддержку. </summary>
[Route("api/v1/admin/support-messages")]
[ApiController]
[Authorize(Policy = AdminAuthorizationPolicies.ViewContent)]
public sealed class SupportMessagesApiController(
    IAdminSupportMessageService adminSupportMessageService,
    IAuthTokenService authTokenService,
    ILogger<SupportMessagesApiController> logger) : ControllerBase
{
    /// <summary> Возвращает постраничный список обращений с фильтрами. </summary>
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? status = null, [FromQuery] string? search = null,
        [FromQuery] string? source = null, [FromQuery] int page = 1)
    {
        var result = await adminSupportMessageService.GetPageAsync(status, search, source, page);

        return Ok(result);
    }

    /// <summary> Возвращает обращение по идентификатору. </summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var message = await adminSupportMessageService.GetAsync(id);

        return message == null ? NotFound() : Ok(message);
    }

    /// <summary> Скачивает вложение после проверки права просмотра обращений. </summary>
    [HttpGet("attachments/{id:guid}")]
    public async Task<IActionResult> Download(
        Guid id,
        [FromServices] ISupportAttachmentService attachments,
        CancellationToken cancellationToken)
    {
        var file = await attachments.GetForAdminAsync(id, cancellationToken);
        if (file == null)
        {
            return this.NotFound();
        }

        this.Response.Headers.CacheControl = "no-store";
        this.Response.Headers.XContentTypeOptions = "nosniff";

        return this.File(file.Content, "application/octet-stream", file.FileName);
    }

    /// <summary> Обновляет данные обращения. </summary>
    [HttpPut("{id:int}")]
    [Authorize(Policy = AdminAuthorizationPolicies.ManageContent)]
    public async Task<IActionResult> Update(int id, [FromBody] AdminSupportMessageUpdateDto dto)
    {
        var result = await adminSupportMessageService.UpdateAsync(id, dto.StatusCode, dto.AdminComment);
        if (result.NotFound)
        {
            return NotFound(new { message = result.ErrorMessage });
        }

        if (!result.Succeeded)
        {
            return BadRequest(new { message = result.ErrorMessage });
        }

        return Ok(new { message = result.SuccessMessage });
    }

    /// <summary> Отправляет ответ администратора на обращение. </summary>
    [HttpPost("{id:int}/replies")]
    [Authorize(Policy = AdminAuthorizationPolicies.ManageContent)]
    public async Task<IActionResult> Reply(int id, [FromBody] SupportReplyRequest request)
    {
        var user = await authTokenService.GetCurrentUserAsync(User);
        if (user == null)
        {
            logger.LogWarning(
                "Не удалось определить администратора для ответа на обращение. SupportMessageId: {SupportMessageId}, IsAuthenticated: {IsAuthenticated}, HasUserIdClaim: {HasUserIdClaim}, TraceId: {TraceId}",
                id, User.Identity?.IsAuthenticated,
                User.HasClaim(claim => claim.Type == "UserID"
                    || claim.Type == System.Security.Claims.ClaimTypes.NameIdentifier
                    || claim.Type == "sub"
                    || claim.Type == "nameid"),
                HttpContext.TraceIdentifier);

            return Unauthorized();
        }

        var result = await adminSupportMessageService.ReplyAsync(id, user.Id, request.Text);
        if (result.NotFound)
        {
            return NotFound(new { message = result.ErrorMessage });
        }

        if (!result.Succeeded)
        {
            return BadRequest(new { message = result.ErrorMessage });
        }

        return Ok(new { message = result.SuccessMessage });
    }
}

/// <summary> Запрос с текстом ответа на обращение. </summary>
public sealed class SupportReplyRequest
{
    /// <summary> Текст ответа поддержки. </summary>
    public string? Text { get; set; }
}
