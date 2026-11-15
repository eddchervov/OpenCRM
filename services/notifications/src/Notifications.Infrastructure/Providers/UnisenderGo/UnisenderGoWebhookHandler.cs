using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Notifications.Application;
using Notifications.Domain;

namespace Notifications.Infrastructure.Providers.UnisenderGo;

/// <summary>Сохраняет проверенный вебхук и адресные результаты в одной транзакции.</summary>
public sealed class UnisenderGoWebhookHandler(
    NotificationDbContext context,
    IIntegrationEventPublisher publisher,
    IOptions<UnisenderGoOptions> options,
    IOptions<NotificationMessagingOptions> messaging)
{
    /// <summary> Обрабатывает запрос вебхука Unisender Go. </summary>
    public async Task<IResult> HandleAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(request.Body);
        var rawBody = await reader.ReadToEndAsync(cancellationToken);
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(rawBody);
        }
        catch (JsonException)
        {
            return Results.BadRequest();
        }

        if (root is not JsonObject || !UnisenderGoWebhookParser.VerifySignature(root, rawBody, options.Value.WebhookKey))
        {
            return Results.Unauthorized();
        }

        IReadOnlyList<Notifications.Contracts.NotificationDeliveryReported> events;
        try
        {
            events = UnisenderGoWebhookParser.Parse(root);
        }
        catch (ArgumentException)
        {
            return Results.BadRequest();
        }

        if (events.Any(item => !messaging.Value.Applications.Contains(item.ApplicationId, StringComparer.Ordinal)))
        {
            return Results.BadRequest();
        }

        var orderedEvents = events.DistinctBy(item => item.EventId).OrderBy(item => item.EventId).ToArray();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        foreach (var item in orderedEvents)
        {
            // Блокировка закрывает гонку двух одинаковых HTTP-запросов до вставки уникального идентификатора.
            var lockKey = BitConverter.ToInt64(item.EventId.ToByteArray());
            await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);
            if (await context.ProviderWebhookEvents.AnyAsync(x => x.Id == item.EventId, cancellationToken))
            {
                continue;
            }

            context.ProviderWebhookEvents.Add(new ProviderWebhookEvent
            {
                Id = item.EventId,
                ApplicationId = item.ApplicationId,
                Payload = JsonSerializer.Serialize(item),
                ReceivedAt = DateTimeOffset.UtcNow
            });
            await publisher.SendToApplicationAsync(item.ApplicationId, item, cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Results.Ok(new { status = "success" });
    }
}
