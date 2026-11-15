using OpenCRM.Services.Interfaces.Payments;

namespace OpenCRM.WebApp.Infrastructure.Billing;

// Тип сохранён для десериализации уже поставленных в очередь заданий Hangfire.
/// <summary> Обёртка для вызова автоматизации биллинга в фоне. </summary>
public sealed class BillingAutomationService(IBillingAutomationService billingAutomationService, IConfiguration configuration)
{
    /// <summary> Запускает сверку платежей. </summary>
    public Task ReconcilePaymentsAsync()
    {
        return configuration.GetValue("PlatformPolicy:Commercial:Enabled", false)
            ? billingAutomationService.ReconcilePaymentsAsync()
            : Task.CompletedTask;
    }
}
