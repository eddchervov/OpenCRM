using MassTransit;
using Payments.Contracts;
using Payments.Service.Persistence;

namespace Payments.Service.Messaging;

public sealed class PaymentCommandConsumer(
    PaymentCommandService commands,
    PaymentDbContext database,
    TimeProvider clock,
    ILogger<PaymentCommandConsumer> logger)
    : IConsumer<CreatePayment>, IConsumer<RequestRefund>
{
    public async Task Consume(ConsumeContext<CreatePayment> context)
    {
        try
        {
            await commands.AcceptAsync(context.Message, context.CancellationToken);
        }
        catch (PaymentCommandException exception)
        {
            logger.LogWarning("Платёжная команда отклонена: {ReasonCode}; {ApplicationId} {PurchaseId} {CommandId}.",
                exception.Code, context.Message.ApplicationId, context.Message.PurchaseId, context.Message.CommandId);
            await this.RejectAsync(context, context.Message.ApplicationId, context.Message.PurchaseId,
                context.Message.CommandId, exception.Code);
        }
        catch (Exception exception)
        {
            logger.LogError("Ошибка обработки платёжной команды {ApplicationId} {PurchaseId} {CommandId} {ErrorType}.",
                context.Message.ApplicationId, context.Message.PurchaseId, context.Message.CommandId,
                exception.GetType().Name);
            throw;
        }
    }

    public async Task Consume(ConsumeContext<RequestRefund> context)
    {
        try
        {
            await commands.AcceptAsync(context.Message, context.CancellationToken);
        }
        catch (PaymentCommandException exception)
        {
            logger.LogWarning("Команда возврата отклонена: {ReasonCode}; {ApplicationId} {PurchaseId} {CommandId}.",
                exception.Code, context.Message.ApplicationId, context.Message.PurchaseId, context.Message.CommandId);
            await this.RejectAsync(context, context.Message.ApplicationId, context.Message.PurchaseId,
                context.Message.CommandId, exception.Code);
        }
        catch (Exception exception)
        {
            logger.LogError("Ошибка обработки команды возврата {ApplicationId} {PurchaseId} {CommandId} {ErrorType}.",
                context.Message.ApplicationId, context.Message.PurchaseId, context.Message.CommandId,
                exception.GetType().Name);
            throw;
        }
    }

    private async Task RejectAsync<T>(ConsumeContext<T> context, string applicationId, Guid purchaseId,
        Guid commandId, string reason) where T : class
    {
        var endpoint = await context.GetSendEndpoint(new Uri("queue:payments-opencrm-events"));
        var eventId = commandId != Guid.Empty ? commandId : context.MessageId ?? Guid.NewGuid();
        await endpoint.Send(new PaymentCommandRejected(eventId, applicationId, purchaseId,
            commandId, reason, clock.GetUtcNow()), context.CancellationToken);
        await database.SaveChangesAsync(context.CancellationToken);
    }
}
