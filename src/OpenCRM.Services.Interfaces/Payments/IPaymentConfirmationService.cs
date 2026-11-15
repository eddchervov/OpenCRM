namespace OpenCRM.Services.Interfaces.Payments;

/// <summary> Контракт сервиса подтверждения платежей. </summary>
public interface IPaymentConfirmationService
{
    /// <summary> Подтверждает платёж по его идентификатору. </summary>
    Task<bool> ConfirmAsync(string paymentId);
}
