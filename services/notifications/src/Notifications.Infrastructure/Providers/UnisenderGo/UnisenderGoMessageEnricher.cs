using System.Text.Json;
using Microsoft.Extensions.Options;
using MimeKit;
using Notifications.Infrastructure.Providers.Smtp;

namespace Notifications.Infrastructure.Providers.UnisenderGo;

/// <summary>Связывает вебхуки провайдера с ключом уведомления исходного приложения.</summary>
public sealed class UnisenderGoMessageEnricher(IOptions<UnisenderGoOptions> options) : IEmailMessageEnricher
{
    /// <summary> Добавляет служебные данные Unisender Go в письмо. </summary>
    public void Enrich(MimeMessage message, string? idempotencyKey, string? applicationId = null)
    {
        var metadata = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            metadata["notification_key"] = idempotencyKey;
        }

        if (!string.IsNullOrWhiteSpace(applicationId))
        {
            metadata["application_id"] = applicationId;
        }

        message.Headers.Add("X-UNISENDER-GO", JsonSerializer.Serialize(new
        {
            track_links = options.Value.TrackLinks ? 1 : 0,
            track_read = options.Value.TrackRead ? 1 : 0,
            global_metadata = metadata
        }));
    }
}
