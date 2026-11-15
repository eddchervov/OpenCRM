using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OpenCRM.WebApp.Infrastructure.Security;
using OpenCRM.Modules.Communications.Contracts;
using OpenCRM.Modules.Communications.Contracts.Dtos;
using OpenCRM.Modules.Notifications;
using Notifications.Contracts;

namespace OpenCRM.WebApp.Controllers.Api.V1.Organizations;

/// <summary> Защищённая граница доставки: очередь содержит только идентификаторы, секреты выдаются текущей аренде. </summary>
[ApiController]
[AllowAnonymous]
[Route("api/internal/conversation-deliveries")]
public sealed class ConversationDeliveriesApiController(
    IConfiguration configuration,
    IConversationDeliveryOutbox deliveries,
    IOrganizationChannelCredentialReader credentials,
    IConversationAttachmentService attachments) : ControllerBase
{
    private readonly IConfiguration _configuration = configuration;
    private readonly IConversationDeliveryOutbox _deliveries = deliveries;
    private readonly IOrganizationChannelCredentialReader _credentials = credentials;
    private readonly IConversationAttachmentService _attachments = attachments;

    [HttpPost("{messageId:guid}/claim")]
    public async Task<IActionResult> Claim(Guid messageId, ConversationDeliveryClaimRequest request, CancellationToken cancellationToken)
    {
        if (!this.IsAuthorized())
        {
            return this.Unauthorized();
        }

        if (request.RequestId == Guid.Empty)
        {
            return this.BadRequest();
        }

        ConversationDeliveryLeaseDto? lease;
        try
        {
            lease = await this._deliveries.ClaimByMessageIdAsync(messageId, request.RequestId, TimeSpan.FromMinutes(3), cancellationToken);
        }
        catch (ConversationDeliveryDeferredException exception)
        {
            this.Response.Headers.RetryAfter = Math.Ceiling(exception.RetryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);

            return this.Conflict(new { code = exception.ReasonCode, message = exception.Message });
        }
        if (lease is null)
        {
            return this.NoContent();
        }

        var credential = await this._credentials.GetActiveCredentialAsync(lease.OrganizationId, lease.ConnectionId, cancellationToken);
        if (credential is null)
        {
            await this._deliveries.CompleteAsync(messageId, lease.LeaseId, new ConversationDeliveryResultDto
            {
                Status = ConversationMessageStatus.Cancelled,
                ErrorCode = "bot-unavailable",
                ErrorMessage = "Бот организации отключён или заменён.",
                CompletedAt = DateTime.UtcNow
            }, cancellationToken);

            return this.StatusCode(StatusCodes.Status410Gone);
        }

        var provider = credential.ProviderCode switch
        {
            "telegram" => ConversationDeliveryProvider.Telegram,
            "max" => ConversationDeliveryProvider.Max,
            _ => throw new InvalidOperationException("Неизвестный провайдер подключённого канала.")
        };

        return this.Ok(new ConversationDeliveryClaimResponse
        {
            MessageId = lease.MessageId,
            LeaseId = lease.LeaseId,
            IsRecovery = lease.IsRecovery,
            OrganizationId = lease.OrganizationId,
            ProviderCode = provider,
            ConnectionId = lease.ConnectionId,
            ProviderBotId = credential.ProviderBotId,
            DeliveryAddress = lease.DeliveryAddress,
            ExternalConversationId = lease.ExternalConversationId,
            BotUsername = credential.Username,
            RequestId = lease.RequestId,
            Token = credential.Token,
            Text = lease.Text,
            Attachments = lease.Attachments.Select(item => new ConversationDeliveryAttachment
            {
                AttachmentId = item.Id,
                Kind = item.Kind,
                FileName = item.FileName,
                ContentType = item.ContentType,
                SizeBytes = item.SizeBytes,
                SortOrder = item.SortOrder
            }).ToList()
        });
    }

    [HttpPost("{messageId:guid}/complete")]
    public async Task<IActionResult> Complete(Guid messageId, ConversationDeliveryCompleteRequest request, CancellationToken cancellationToken)
    {
        if (!this.IsAuthorized())
        {
            return this.Unauthorized();
        }

        var status = request.Result.Status switch
        {
            "accepted" => ConversationMessageStatus.Accepted,
            "failed" => ConversationMessageStatus.Failed,
            "uncertain" => ConversationMessageStatus.Uncertain,
            _ => (ConversationMessageStatus?)null
        };
        if (!status.HasValue || request.LeaseId == Guid.Empty)
        {
            return this.BadRequest();
        }

        var completed = await this._deliveries.CompleteAsync(messageId, request.LeaseId, new ConversationDeliveryResultDto
        {
            Status = status.Value,
            ProviderMessageId = request.Result.ProviderMessageId,
            ErrorCode = request.Result.ErrorCode,
            ErrorMessage = request.Result.ErrorMessage,
            SafeToRetry = request.Result.SafeToRetry,
            RetryAfterSeconds = request.Result.RetryAfterSeconds,
            CompletedAt = request.Result.CompletedAt
        }, cancellationToken);

        return completed ? this.NoContent() : this.Conflict();
    }

    [HttpGet("{messageId:guid}/attachments/{attachmentId:guid}")]
    [EnableRateLimiting(RateLimitPolicyNames.ConversationFiles)]
    public async Task<IActionResult> Attachment(Guid messageId, Guid attachmentId, CancellationToken cancellationToken)
    {
        if (!this.IsAuthorized())
        {
            return this.Unauthorized();
        }

        if (!Guid.TryParse(this.Request.Headers["X-Conversation-Lease-Id"], out var leaseId))
        {
            return this.BadRequest();
        }

        var attachment = await this._attachments.GetForDeliveryAsync(messageId, leaseId, attachmentId, cancellationToken);
        if (attachment?.Content is null
            || !attachment.ConnectionId.HasValue
            || await this._credentials.GetActiveCredentialAsync(attachment.OrganizationId, attachment.ConnectionId.Value, cancellationToken) is null)
        {
            return this.NotFound();
        }

        this.Response.Headers["X-Content-Type-Options"] = "nosniff";

        return this.File(attachment.Content, "application/octet-stream", attachment.FileName);
    }

    private bool IsAuthorized()
    {
        this.Response.Headers.CacheControl = "no-store";
        var configuredKey = this._configuration["TelegramOrganizationNotifications:ServiceKey"];
        var suppliedKey = this.Request.Headers["X-Notification-Service-Key"].ToString();

        return this.Request.IsHttps && configuredKey is { Length: >= 32 } && !string.IsNullOrWhiteSpace(suppliedKey)
            && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(configuredKey), Encoding.UTF8.GetBytes(suppliedKey));
    }
}
