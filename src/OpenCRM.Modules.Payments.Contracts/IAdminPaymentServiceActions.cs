namespace OpenCRM.Modules.Payments.Contracts;

/// <summary> Единые действия веб- и десктопной админки с платежами отдельного сервиса. </summary>
public interface IAdminPaymentServiceActions
{
    Task<AdminPaymentServiceActions?> GetAsync(int paymentId, CancellationToken cancellationToken = default);

    Task<AdminPaymentServiceActionResult> ExecuteAsync(
        int paymentId,
        AdminPaymentServiceActionRequest request,
        string actorUserId,
        CancellationToken cancellationToken = default);
}
