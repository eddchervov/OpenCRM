using MimeKit;

namespace Notifications.Infrastructure.Providers.Smtp;

/// <summary>Добавляет служебные данные выбранного почтового провайдера.</summary>
public interface IEmailMessageEnricher
{
    /// <summary> Добавляет служебные данные почтового провайдера в письмо. </summary>
    void Enrich(MimeMessage message, string? idempotencyKey, string? applicationId = null);
}
