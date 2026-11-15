using System.Text.RegularExpressions;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Notifications.Application;
using Notifications.Domain;

namespace Notifications.Infrastructure.Providers.Smtp;

/// <summary>
/// Отправляет электронные письма через SMTP или имитирует доставку в безопасном режиме.
/// </summary>
public sealed partial class EmailNotificationSender(
    IOptions<DeliveryOptions> deliveryOptions,
    IOptions<SmtpOptions> smtpOptions,
    ILogger<EmailNotificationSender> logger,
    IEnumerable<IEmailMessageEnricher> enrichers) : INotificationChannelSender
{
    /// <summary>Обслуживает канал электронной почты.</summary>
    public NotificationChannel Channel => NotificationChannel.Email;

    /// <summary>Формирует MIME-сообщение и передаёт его настроенному SMTP-серверу.</summary>
    public async Task<string?> SendAsync(
        string recipient,
        string? subject,
        string body,
        bool isHtml,
        CancellationToken cancellationToken,
        string? idempotencyKey = null, string? applicationId = null)
    {
        // Режим по умолчанию исключает случайную отправку реальным пользователям при разработке.
        if (deliveryOptions.Value.LogOnly)
        {
            LogSimulatedDelivery(logger);
            return $"log:{Guid.NewGuid():N}";
        }

        var options = smtpOptions.Value;
        if (string.IsNullOrWhiteSpace(options.Host)
            || string.IsNullOrWhiteSpace(options.FromAddress))
        {
            throw new InvalidOperationException("SMTP provider is not configured.");
        }

        using var message = new MimeMessage();
        message.From.Add(new MailboxAddress(options.FromName, options.FromAddress));
        message.To.Add(MailboxAddress.Parse(recipient));
        message.Subject = subject ?? string.Empty;
        message.Body = isHtml
            ? new BodyBuilder { HtmlBody = body, TextBody = NotificationText.FromHtml(body) }.ToMessageBody()
            : new TextPart("plain") { Text = body };
        foreach (var enricher in enrichers)
        {
            enricher.Enrich(message, idempotencyKey, applicationId);
        }

        using var client = new SmtpClient();
        var socketOptions = options.UseSsl
            ? SecureSocketOptions.SslOnConnect
            : SecureSocketOptions.StartTlsWhenAvailable;

        await client.ConnectAsync(options.Host, options.Port, socketOptions, cancellationToken);
        if (!string.IsNullOrWhiteSpace(options.Username))
        {
            await client.AuthenticateAsync(options.Username, options.Password, cancellationToken);
        }

        var messageId = await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);

        return ExtractProviderMessageId(messageId);
    }

    /// <summary> Извлекает идентификатор сообщения провайдера из ответа. </summary>
    private static string? ExtractProviderMessageId(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return null;
        }

        var match = Regex.Match(response, @"(?:Job_Id|Job-Id|job_id|id)\s*[:=]\s*([^\s,;]+)", RegexOptions.IgnoreCase);
        var value = match.Success ? match.Groups[1].Value.Trim('<', '>', '"', '\'') : response.Trim();

        return value.Length > 200 ? value[..200] : value;
    }

    /// <summary> Логирует имитацию отправки email в режиме LogOnly. </summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "Отправка email имитирована в режиме LogOnly.")]
    private static partial void LogSimulatedDelivery(ILogger logger);
}
