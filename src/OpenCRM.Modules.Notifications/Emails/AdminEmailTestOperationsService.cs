using System.Text.Json;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Admin.Dtos;

namespace OpenCRM.Modules.Notifications.Emails;

/// <summary> Сервис тестовых операций с email для администратора. </summary>
public sealed class AdminEmailTestOperationsService(
    OpenCRMContext context,
    IEmailSender emailSender,
    Helpers.ProjectBrand? brand = null) : IAdminEmailTestOperationsService
{
    /// <summary> Отправляет тестовое письмо. </summary>
    public async Task<EmailAdminOperationResult<string>> SendTestEmailAsync(EmailTestSendRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return EmailAdminOperationResult<string>.Failure("Укажите email для тестовой отправки.");
        }

        var subject = string.IsNullOrWhiteSpace(request.Subject)
            ? $"Тестовое письмо {brand?.DisplayName ?? Helpers.ProjectBrand.DefaultName}"
            : request.Subject.Trim();
        var body = string.IsNullOrWhiteSpace(request.HtmlBody)
            ? $"""
              <p>Это тестовое письмо из админки {System.Text.Encodings.Web.HtmlEncoder.Default.Encode(brand?.DisplayName ?? Helpers.ProjectBrand.DefaultName)}.</p>
              <p>Результаты отправки и доставки появятся в журнале email.</p>
              """
            : request.HtmlBody.Trim();

        try
        {
            await emailSender.SendEmailAsync(request.Email.Trim(), subject, body);
            return EmailAdminOperationResult<string>.Success(
                "Тестовое письмо поставлено в очередь. Результат появится в журнале email.");
        }
        catch (Exception exception)
        {
            return EmailAdminOperationResult<string>.Failure(
                $"Не удалось отправить тестовое письмо: {exception.Message}");
        }
    }

    /// <summary> Создаёт тестовое событие webhook для email-сообщения. </summary>
    public async Task<EmailAdminOperationResult<EmailMessageEventItem>> CreateWebhookTestAsync(
        EmailWebhookTestRequest request,
        CancellationToken cancellationToken = default)
    {
        var status = AdminEmailTestOperationsService.NormalizeStatusForTest(request.Status);
        EmailMessage? message = null;

        if (request.EmailMessageId.HasValue)
        {
            message = await context.EmailMessages
                .FirstOrDefaultAsync(x => x.Id == request.EmailMessageId.Value, cancellationToken);
        }

        message ??= await context.EmailMessages
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (message == null)
        {
            return EmailAdminOperationResult<EmailMessageEventItem>.Failure(
                "Нет писем, к которым можно привязать тестовый вебхук.");
        }

        var eventAt = DateTime.UtcNow;
        var rawPayload = JsonSerializer.Serialize(new
        {
            source = "admin_manual_test",
            event_name = status,
            event_time = eventAt,
            event_data = new
            {
                email_message_id = message.Id,
                email = message.ToEmail,
                message_id = message.ProviderMessageId ?? $"manual-{message.Id}",
                status
            }
        });

        var messageEvent = new EmailMessageEvent
        {
            EmailMessageId = message.Id,
            Provider = "manual-test",
            EventName = status,
            Status = status,
            Email = message.ToEmail,
            ProviderMessageId = message.ProviderMessageId ?? $"manual-{message.Id}",
            RawPayload = rawPayload,
            SignatureVerified = false,
            EventAt = eventAt,
            ReceivedAt = eventAt
        };

        context.EmailMessageEvents.Add(messageEvent);
        AdminEmailTestOperationsService.ApplyTestEventToMessage(message, status, eventAt, messageEvent.ProviderMessageId);
        await context.SaveChangesAsync(cancellationToken);

        return EmailAdminOperationResult<EmailMessageEventItem>.Success(new EmailMessageEventItem
        {
            Id = messageEvent.Id,
            EmailMessageId = messageEvent.EmailMessageId,
            Provider = messageEvent.Provider,
            EventName = messageEvent.EventName,
            Status = messageEvent.Status,
            Email = messageEvent.Email,
            ProviderMessageId = messageEvent.ProviderMessageId,
            RawPayload = messageEvent.RawPayload,
            SignatureVerified = messageEvent.SignatureVerified,
            EventAt = messageEvent.EventAt,
            ReceivedAt = messageEvent.ReceivedAt
        });
    }

    /// <summary> Нормализует статус для тестового события. </summary>
    private static string NormalizeStatusForTest(string? status)
    {
        var normalized = string.IsNullOrWhiteSpace(status)
            ? EmailMessageStatusCodes.Delivered
            : status.Trim().ToLowerInvariant();

        return EmailMessageStatusCodes.All.Contains(normalized)
            ? normalized
            : EmailMessageStatusCodes.Delivered;
    }

    /// <summary> Применяет тестовое событие к email-сообщению. </summary>
    private static void ApplyTestEventToMessage(EmailMessage message, string status, DateTime eventAt, string providerMessageId)
    {
        message.Provider = "manual-test";
        message.ProviderMessageId ??= providerMessageId;
        message.LastEventCode = status;
        message.LastEventAt = eventAt;
        message.StatusCode = status;

        if (status == EmailMessageStatusCodes.Opened)
        {
            message.OpenCount++;
        }
        else if (status == EmailMessageStatusCodes.Clicked)
        {
            message.ClickCount++;
        }
    }
}
