using MassTransit;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using Payments.Contracts;
using Payments.Service.Domain;
using Payments.Service.Persistence;

namespace Payments.Service.Processing;

public sealed class PaymentEventPublisher(
    PaymentDbContext dbContext,
    ISendEndpointProvider sendEndpointProvider,
    TimeProvider timeProvider)
{
    public async Task PublishStateAsync(PaymentOrder order, CancellationToken cancellationToken)
    {
        var creationOperation = order.Operations
            .Where(operation => operation.Kind == OperationKind.CreatePayment)
            .OrderBy(operation => operation.CreatedAt)
            .FirstOrDefault()
            ?? dbContext.ProviderOperations.Local
                .Where(operation => operation.PaymentOrderId == order.Id && operation.Kind == OperationKind.CreatePayment)
                .OrderBy(operation => operation.CreatedAt)
                .FirstOrDefault();
        var creationStatus = creationOperation?.Status
            ?? await dbContext.ProviderOperations
                .Where(operation => operation.PaymentOrderId == order.Id && operation.Kind == OperationKind.CreatePayment)
                .OrderBy(operation => operation.CreatedAt)
                .Select(operation => (OperationStatus?)operation.Status)
                .FirstOrDefaultAsync(cancellationToken);
        order.Version++;
        var endpoint = await sendEndpointProvider.GetSendEndpoint(new Uri($"queue:payments-{order.ApplicationId}-events"));
        await endpoint.Send(new PaymentStateChanged(
            Guid.NewGuid(),
            order.ApplicationId,
            order.PurchaseId,
            order.Id,
            order.Version,
            creationStatus?.ToString() ?? "NotCreated",
            order.Status.ToString(),
            order.AmountMinor,
            order.RefundedAmountMinor,
            order.Currency,
            order.ProviderPaymentId,
            order.ConfirmationUrl,
            timeProvider.GetUtcNow(),
            order.IsTest), cancellationToken);
    }

    public async Task PublishOperationStateAsync(
        PaymentOrder order,
        ProviderOperation operation,
        CancellationToken cancellationToken)
    {
        var endpoint = await sendEndpointProvider.GetSendEndpoint(new Uri($"queue:payments-{order.ApplicationId}-events"));
        await endpoint.Send(new PaymentOperationStateChanged(
            PaymentEventPublisher.GetOperationEventId(operation),
            order.ApplicationId,
            order.PurchaseId,
            order.Id,
            operation.Id,
            operation.CommandId,
            operation.Kind.ToString(),
            operation.Status.ToString(),
            operation.LastError,
            operation.UpdatedAt), cancellationToken);
    }

    public async Task PublishCommandRejectedAsync(
        string applicationId,
        Guid purchaseId,
        Guid commandId,
        string reason,
        CancellationToken cancellationToken)
    {
        var endpoint = await sendEndpointProvider.GetSendEndpoint(new Uri($"queue:payments-{applicationId}-events"));
        await endpoint.Send(new PaymentCommandRejected(
            commandId,
            applicationId,
            purchaseId,
            commandId,
            reason,
            timeProvider.GetUtcNow()), cancellationToken);
    }

    private static Guid GetOperationEventId(ProviderOperation operation)
    {
        var value = Encoding.UTF8.GetBytes(
            $"{operation.Id:D}:{operation.Status}:{operation.AttemptCount}:{operation.LastError}:{operation.UpdatedAt.UtcDateTime.Ticks}");
        var hash = SHA256.HashData(value);

        return new Guid(hash.AsSpan(0, 16));
    }
}
