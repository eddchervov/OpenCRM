using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Communications.Contracts;
using OpenCRM.Modules.Notifications;
using OpenCRM.WebApp.Infrastructure.Security;

namespace OpenCRM.WebApp.Controllers.Api.V1.Organizations;

/// <summary> Авторизованный доступ к вложениям переписки без публичных URL и токенов мессенджеров. </summary>
[ApiController]
[Authorize]
[EnableRateLimiting(RateLimitPolicyNames.ConversationFiles)]
[Route("api/v1/organizations/{organizationId:guid}/conversations")]
public sealed class ConversationAttachmentsApiController(
    UserManager<ApplicationUser> users,
    IConversationAttachmentService attachments,
    IOrganizationChannelFileReader channelFiles) : ControllerBase
{
    private readonly UserManager<ApplicationUser> _users = users;
    private readonly IConversationAttachmentService _attachments = attachments;
    private readonly IOrganizationChannelFileReader _channelFiles = channelFiles;

    [HttpPost("{conversationId:guid}/attachments")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(21 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 21 * 1024 * 1024)]
    public async Task<IActionResult> Upload(Guid organizationId, Guid conversationId, IFormFile file, CancellationToken cancellationToken)
    {
        var userId = this._users.GetUserId(this.User);
        if (string.IsNullOrEmpty(userId))
        {
            return this.Unauthorized();
        }

        this.Response.Headers.CacheControl = "no-store";
        try
        {
            await using var stream = file.OpenReadStream();
            var result = await this._attachments.UploadAsync(userId, organizationId, conversationId, file.FileName,
                file.ContentType, file.Length, stream, cancellationToken);

            return this.Ok(result);
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
    }

    [HttpGet("attachments/{attachmentId:guid}")]
    public async Task<IActionResult> Download(Guid organizationId, Guid attachmentId, CancellationToken cancellationToken)
    {
        var userId = this._users.GetUserId(this.User);
        if (string.IsNullOrEmpty(userId))
        {
            return this.Unauthorized();
        }

        this.Response.Headers.CacheControl = "no-store";
        this.Response.Headers["X-Content-Type-Options"] = "nosniff";
        try
        {
            var attachment = await this._attachments.GetAsync(userId, organizationId, attachmentId, cancellationToken);
            if (attachment is null)
            {
                return this.NotFound();
            }

            if (attachment.SizeBytes > 20 * 1024 * 1024)
            {
                return this.StatusCode(StatusCodes.Status413PayloadTooLarge, new { message = "Размер вложения превышает 20 МБ. Метаданные сохранены в истории." });
            }

            if (attachment.Content is not null)
            {
                return this.File(attachment.Content, "application/octet-stream", attachment.FileName);
            }

            if (string.IsNullOrEmpty(attachment.ProviderFileId) || !attachment.ConnectionId.HasValue)
            {
                return this.NotFound();
            }

            await using var download = await this._channelFiles.OpenAsync(organizationId, attachment.ConnectionId.Value, attachment.ProviderFileId, cancellationToken);
            if (download is null)
            {
                return this.NotFound(new { message = "Вложение недоступно через текущего бота организации." });
            }

            var disposition = new ContentDispositionHeaderValue("attachment");
            disposition.SetHttpFileName(attachment.FileName);
            this.Response.Headers.ContentDisposition = disposition.ToString();
            this.Response.ContentType = "application/octet-stream";
            await download.Content.CopyToAsync(this.Response.Body, cancellationToken);

            return new EmptyResult();
        }
        catch (UnauthorizedAccessException)
        {
            return this.Forbid();
        }
    }
}
