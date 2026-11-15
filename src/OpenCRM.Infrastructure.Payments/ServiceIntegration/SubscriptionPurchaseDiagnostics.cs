using Microsoft.Extensions.Logging;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;

namespace OpenCRM.Infrastructure.Payments.ServiceIntegration;

/// <summary> Безопасные коды ошибок и долговечные записи финансовых переходов без секретов и HTTP-тел. </summary>
internal static class SubscriptionPurchaseDiagnostics
{
    public static void Add(OpenCRMContext context, SubscriptionPurchase purchase, string stage, string code,
        DateTime now, Guid? commandId = null)
    {
        context.SubscriptionPaymentEvents.Add(new SubscriptionPaymentEvent
        {
            PurchaseId = purchase.Id,
            CommandId = commandId,
            ProviderOrderId = purchase.ProviderOrderId,
            ProviderVersion = purchase.ProviderVersion,
            Kind = stage,
            Code = code,
            AmountMinor = purchase.AmountMinor,
            RefundedAmountMinor = purchase.RefundedAmountMinor,
            CreatedAt = now,
            ProcessedAt = now,
            NextAttemptAt = now
        });
    }

    public static void Failure(ILogger logger, string stage, Guid? purchaseId, Exception exception)
    {
        logger.LogError("Сбой финансового процесса. Stage={Stage}, PurchaseId={PurchaseId}, ErrorType={ErrorType}, SqlState={SqlState}",
            stage, purchaseId, exception.GetType().Name, (exception.GetBaseException() as Npgsql.PostgresException)?.SqlState);
    }
}
