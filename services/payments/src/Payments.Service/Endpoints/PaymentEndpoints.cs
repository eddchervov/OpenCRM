using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Payments.Service.Configuration;
using Payments.Service.Domain;
using Payments.Service.Persistence;

namespace Payments.Service.Endpoints;

public static class PaymentEndpoints
{
    public static void MapPaymentEndpoints(this WebApplication application)
    {
        application.MapGet("/internal/payments/{applicationId}/{purchaseId:guid}",
            async (string applicationId, Guid purchaseId, HttpContext http, PaymentDbContext database,
                IOptions<PaymentOptions> options, CancellationToken cancellationToken) =>
            {
                var expected = SHA256.HashData(Encoding.UTF8.GetBytes(options.Value.ServiceKey));
                var actual = SHA256.HashData(Encoding.UTF8.GetBytes(http.Request.Headers["X-Payments-Key"].ToString()));
                if (!CryptographicOperations.FixedTimeEquals(expected, actual))
                {
                    return Results.Unauthorized();
                }

                if (applicationId != options.Value.ApplicationId)
                {
                    return Results.NotFound();
                }

                var order = await database.PaymentOrders.AsNoTracking()
                    .Where(item => item.ApplicationId == applicationId && item.PurchaseId == purchaseId)
                    .Select(item => new
                    {
                        item.Id,
                        item.ApplicationId,
                        item.PurchaseId,
                        item.Version,
                        item.AmountMinor,
                        item.Currency,
                        PaymentStatus = item.Status.ToString(),
                        item.RefundedAmountMinor,
                        item.ProviderPaymentId,
                        item.ConfirmationUrl,
                        item.IsTest,
                        item.LastReconciledAt,
                        item.LastError,
                        Operations = item.Operations.OrderBy(operation => operation.CreatedAt).Select(operation => new
                        {
                            operation.Id,
                            operation.CommandId,
                            operation.UpdatedAt,
                            operation.AttemptCount,
                            Kind = operation.Kind.ToString(),
                            Status = operation.Status.ToString(),
                            operation.AmountMinor,
                            operation.ProviderObjectId,
                            operation.ProviderStatus,
                            operation.LastError
                        })
                    }).SingleOrDefaultAsync(cancellationToken);

                return order == null ? Results.NotFound() : Results.Ok(order);
            });

        application.MapPost("/internal/payments/{applicationId}/{purchaseId:guid}/reconcile",
            async (string applicationId, Guid purchaseId, HttpContext http, PaymentDbContext database,
                IOptions<PaymentOptions> options, TimeProvider clock, ILoggerFactory logs, CancellationToken cancellationToken) =>
            {
                var expected = SHA256.HashData(Encoding.UTF8.GetBytes(options.Value.ServiceKey));
                var actual = SHA256.HashData(Encoding.UTF8.GetBytes(http.Request.Headers["X-Payments-Key"].ToString()));
                if (!CryptographicOperations.FixedTimeEquals(expected, actual))
                {
                    return Results.Unauthorized();
                }

                if (applicationId != options.Value.ApplicationId)
                {
                    return Results.NotFound();
                }

                var order = await database.PaymentOrders.SingleOrDefaultAsync(item => item.ApplicationId == applicationId
                    && item.PurchaseId == purchaseId, cancellationToken);
                if (order == null)
                {
                    return Results.NotFound();
                }

                if (options.Value.Mode == "Disabled" || order.MerchantAccountId != options.Value.MerchantAccountId
                    || order.ProviderPaymentId == null || order.Status == PaymentStatus.Canceled)
                {
                    return Results.Conflict(new { message = "Платёж пока не создан у провайдера или окончательно отменён." });
                }

                var requestedAt = clock.GetUtcNow();
                await database.PaymentOrders.Where(item => item.Id == order.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(item => item.NextCheckAt, requestedAt), cancellationToken);
                logs.CreateLogger(typeof(PaymentEndpoints).FullName!).LogInformation(
                    "Внеочередная проверка запрошена. PurchaseId={PurchaseId}, PaymentOrderId={PaymentOrderId}", purchaseId, order.Id);

                return Results.Accepted(value: new { PurchaseId = purchaseId, RequestedAt = requestedAt });
            });

        application.MapPost("/webhooks/yookassa", PaymentEndpoints.AcceptWebhookAsync)
            .RequireRateLimiting("webhooks");
    }

    private static async Task<IResult> AcceptWebhookAsync(HttpRequest request, PaymentDbContext database,
        TimeProvider clock, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(typeof(PaymentEndpoints).FullName!);
        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(request.Body,
                new JsonDocumentOptions { MaxDepth = 16 }, cancellationToken);
        }
        catch (JsonException)
        {
            return Results.BadRequest();
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String
                || type.GetString() != "notification"
                || !root.TryGetProperty("event", out var eventProperty) || eventProperty.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("object", out var objectProperty) || objectProperty.ValueKind != JsonValueKind.Object
                || !objectProperty.TryGetProperty("id", out var idProperty) || idProperty.ValueKind != JsonValueKind.String)
            {
                return Results.BadRequest();
            }

            var eventType = eventProperty.GetString();
            var objectId = idProperty.GetString();
            if (eventType is not ("payment.succeeded" or "payment.canceled" or "payment.waiting_for_capture" or "refund.succeeded")
                || string.IsNullOrWhiteSpace(objectId) || objectId.Length > 128
                || objectId.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')))
            {
                return Results.BadRequest();
            }

            if (await database.WebhookNotifications.AnyAsync(item => item.EventType == eventType
                && item.ObjectId == objectId, cancellationToken))
            {
                return Results.Ok();
            }

            var now = clock.GetUtcNow();
            var notification = new WebhookNotification
            {
                Id = Guid.NewGuid(),
                EventType = eventType,
                ObjectId = objectId,
                ReceivedAt = now,
                NextAttemptAt = now
            };
            database.WebhookNotifications.Add(notification);

            try
            {
                await database.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: "23505" })
            {
                database.ChangeTracker.Clear();
            }
            catch (Exception exception)
            {
                logger.LogError("Не удалось сохранить webhook {WebhookId} {ProviderObjectId} {ErrorType}.",
                    notification.Id, objectId, exception.GetType().Name);
                throw;
            }

            return Results.Ok();
        }
    }
}
