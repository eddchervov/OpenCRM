using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Notifications.Infrastructure.Providers.UnisenderGo;

/// <summary>Подключает только обратный канал провайдера; отправка из приложений остаётся в RabbitMQ.</summary>
public static class UnisenderGoEndpoints
{
    /// <summary> Подключает вебхуки уведомлений Unisender Go. </summary>
    public static void MapNotificationWebhooks(this WebApplication application)
    {
        if (!application.Configuration.GetValue<bool>("UnisenderGo:WebhookEnabled"))
        {
            return;
        }

        application.MapPost("/webhooks/unisender-go",
            (HttpRequest request, UnisenderGoWebhookHandler handler, CancellationToken cancellationToken) =>
                handler.HandleAsync(request, cancellationToken));
    }
}
