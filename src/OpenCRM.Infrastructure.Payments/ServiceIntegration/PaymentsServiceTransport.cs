using System.Text.Json;
using MassTransit;
using Microsoft.Extensions.Options;
using Payments.Contracts;

namespace OpenCRM.Infrastructure.Payments.ServiceIntegration;

/// <summary> Независимая от уведомлений шина финансовых поручений. </summary>
public interface IPaymentsServiceBus : IBus
{
}

/// <summary> Граница доставки неизменяемых поручений для проверки сетевых сбоев. </summary>
internal interface ISubscriptionPaymentCommandTransport
{
    Task SendAsync(string kind, string payload, Guid commandId, Guid purchaseId, CancellationToken cancellationToken);
}

internal sealed class PaymentsServiceCommandTransport(
    IPaymentsServiceBus bus,
    IOptions<PaymentsServiceOptions> options) : ISubscriptionPaymentCommandTransport
{
    public async Task SendAsync(string kind, string payload, Guid commandId, Guid purchaseId, CancellationToken cancellationToken)
    {
        var endpoint = await bus.GetSendEndpoint(new Uri($"queue:{options.Value.CommandsQueueName}"));
        if (kind == "create")
        {
            var command = JsonSerializer.Deserialize<CreatePayment>(payload) ?? throw new JsonException();
            if (command.CommandId != commandId || command.PurchaseId != purchaseId
                || command.ApplicationId != options.Value.ApplicationId)
            {
                throw new JsonException("Реквизиты сохранённой команды не совпадают с покупкой.");
            }

            await endpoint.Send(command, send => { send.MessageId = commandId; send.CorrelationId = purchaseId; }, cancellationToken);
            return;
        }

        if (kind == "refund")
        {
            var command = JsonSerializer.Deserialize<RequestRefund>(payload) ?? throw new JsonException();
            if (command.CommandId != commandId || command.PurchaseId != purchaseId
                || command.ApplicationId != options.Value.ApplicationId)
            {
                throw new JsonException("Реквизиты сохранённого возврата не совпадают с покупкой.");
            }

            await endpoint.Send(command, send => { send.MessageId = commandId; send.CorrelationId = purchaseId; }, cancellationToken);
            return;
        }

        throw new JsonException("Неизвестный тип финансового поручения.");
    }
}

internal sealed class PaymentsServiceResultConsumer(SubscriptionPaymentEventProcessor processor) :
    IConsumer<PaymentStateChanged>, IConsumer<PaymentCommandRejected>, IConsumer<PaymentOperationStateChanged>
{
    public Task Consume(ConsumeContext<PaymentStateChanged> context) => processor.ReceiveAsync(context.Message, context.CancellationToken);
    public Task Consume(ConsumeContext<PaymentCommandRejected> context) => processor.ReceiveAsync(context.Message, context.CancellationToken);
    public Task Consume(ConsumeContext<PaymentOperationStateChanged> context) => processor.ReceiveAsync(context.Message, context.CancellationToken);
}
