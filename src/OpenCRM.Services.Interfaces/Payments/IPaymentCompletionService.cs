using OpenCRM.Services.Interfaces.Payments.Dto;

namespace OpenCRM.Services.Interfaces.Payments;

/// <summary> Контракт сервиса состояния завершения платежа. </summary>
public interface IPaymentCompletionService
{
    /// <summary> Возвращает состояние завершения платежа пользователя. </summary>
    Task<PaymentCompleteDto> GetCompleteStateAsync(string? userId);
}
