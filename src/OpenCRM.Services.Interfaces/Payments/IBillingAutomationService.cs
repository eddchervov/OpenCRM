namespace OpenCRM.Services.Interfaces.Payments;

/// <summary> Контракт сервиса сверки платежей с платёжной системой. </summary>
public interface IBillingAutomationService
{
    /// <summary> Выполняет сверку платежей с платёжной системой. </summary>
    Task ReconcilePaymentsAsync();
}
