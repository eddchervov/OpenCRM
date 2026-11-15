using OpenCRM.Modules.Payments.Contracts;
using OpenCRM.Services.Interfaces.Payments.Dto;

namespace OpenCRM.Modules.Billing.Contracts;

/// <summary> Контракт оформления оплаты тарифа рабочего пространства. </summary>
public interface IOrganizationSubscriptionCheckoutService
{
    /// <summary> Оформляет покупку тарифа организации. </summary>
    Task<PaymentCreationResult> PurchaseAsync(string userId, Guid organizationId, int planId, PaymentBuyerDto? buyer, Guid operationId, string? returnUrl = null);

    /// <summary> Оформляет продление тарифа организации. </summary>
    Task<PaymentCreationResult> ExtendAsync(string userId, Guid organizationId, PaymentBuyerDto? buyer, Guid operationId, string? returnUrl = null);
}
