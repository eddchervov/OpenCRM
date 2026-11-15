using OpenCRM.Modules.Payments.Contracts;
using OpenCRM.Services.Interfaces.Payments.Dto;

namespace OpenCRM.Modules.Billing.Contracts;

/// <summary> Контракт оформления оплаты личного тарифа. </summary>
public interface IPersonalSubscriptionCheckoutService
{
    /// <summary> Оформляет покупку личного тарифа. </summary>
    Task<PaymentCreationResult> PurchaseAsync(string userId, int planId, PaymentBuyerDto? buyer, Guid operationId, string? returnUrl = null);

    /// <summary> Оформляет продление личного тарифа. </summary>
    Task<PaymentCreationResult> ExtendAsync(string userId, PaymentBuyerDto? buyer, Guid operationId, string? returnUrl = null);
}
