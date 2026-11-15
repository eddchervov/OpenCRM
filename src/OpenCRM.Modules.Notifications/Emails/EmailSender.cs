using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Emails;

namespace OpenCRM.Modules.Notifications.Emails;

/// <summary> Сервис постановки email-сообщений в очередь на отправку. </summary>
public sealed class EmailSender(OpenCRMContext context, IConfiguration? configuration = null) : IEmailSender, IEmailNotificationService
{
    /// <summary> Ставит письмо в очередь на отправку. </summary>
    public Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        return this.QueueAsync(email, subject, htmlMessage, Guid.NewGuid().ToString("N"));
    }

    /// <summary> Ставит письмо в очередь с ключом идемпотентности. </summary>
    public async Task QueueAsync(
        string email,
        string subject,
        string html,
        string idempotencyKey,
        Guid? campaignRecipientId = null,
        long? subscriptionReminderId = null)
    {
        this.EnsureDeliveryEnabled();
        if (await context.EmailMessages.AnyAsync(x => x.NotificationKey == idempotencyKey))
        {
            return;
        }

        var message = await this.CreateMessageAsync(email, subject, idempotencyKey);
        message.TextBody = subject;
        message.TemplateKey = "system/custom-email";
        message.TemplateDataJson = JsonSerializer.Serialize(new { subject, htmlBody = html });
        message.CampaignRecipientId = campaignRecipientId;
        message.SubscriptionReminderId = subscriptionReminderId;

        context.EmailMessages.Add(message);
        try
        {
            await context.SaveChangesAsync();
        }
        catch
        {
            context.Entry(message).State = EntityState.Detached;
            throw;
        }
    }

    /// <summary> Ставит письмо по шаблону в очередь с подстановкой данных. </summary>
    public async Task QueueTemplateAsync(
        string email,
        string subject,
        string templateKey,
        object data,
        string? idempotencyKey = null,
        long? subscriptionReminderId = null)
    {
        this.EnsureDeliveryEnabled();
        if (idempotencyKey != null && await context.EmailMessages.AnyAsync(x => x.NotificationKey == idempotencyKey))
        {
            return;
        }

        var message = await this.CreateMessageAsync(email, subject, idempotencyKey ?? Guid.NewGuid().ToString("N"));
        message.SubscriptionReminderId = subscriptionReminderId;
        message.TextBody = subject;
        message.TemplateKey = templateKey;
        message.TemplateDataJson = JsonSerializer.Serialize(data);
        context.EmailMessages.Add(message);
        try
        {
            await context.SaveChangesAsync();
        }
        catch
        {
            context.Entry(message).State = EntityState.Detached;
            throw;
        }
    }

    /// <summary> Создаёт запись email-сообщения с ключом идемпотентности. </summary>
    private async Task<EmailMessage> CreateMessageAsync(string email, string subject, string key)
    {
        if (!EmailAddressValidation.TryValidate(email, out var error))
        {
            throw new ArgumentException(error, nameof(email));
        }

        if (string.IsNullOrWhiteSpace(subject) || subject.Length > 300)
        {
            throw new ArgumentException("Тема письма должна содержать от 1 до 300 символов.", nameof(subject));
        }

        var normalizedEmail = email.Trim().ToUpperInvariant();
        var userId = await context.ApplicationUsers.AsNoTracking()
            .Where(x => x.NormalizedEmail == normalizedEmail)
            .Select(x => x.Id)
            .FirstOrDefaultAsync();

        return new EmailMessage
        {
            UserId = userId,
            ToEmail = email.Trim(),
            Subject = subject,
            NotificationKey = key,
            StatusCode = EmailMessageStatusCodes.Queued,
            NotificationStatus = "pending",
            NextDispatchAt = DateTime.UtcNow
        };
    }

    /// <summary> Не принимает письма, если доставка установки явно выключена. </summary>
    private void EnsureDeliveryEnabled()
    {
        if (string.Equals(configuration?["Notifications:Transport"], "Disabled", StringComparison.OrdinalIgnoreCase)
            || string.Equals(configuration?["Notifications:Transport"], "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Доставка писем не настроена. Подключите сервис уведомлений и задайте Notifications:Transport=RabbitMq.");
        }
    }

}
