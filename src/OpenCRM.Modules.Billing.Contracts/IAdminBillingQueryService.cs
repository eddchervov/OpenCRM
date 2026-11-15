using OpenCRM.Modules.Billing.Contracts.Dtos;

namespace OpenCRM.Modules.Billing.Contracts;

/// <summary>Читает административные отчёты биллинга без изменения платежей и файлов чеков.</summary>
public interface IAdminBillingQueryService
{
    /// <summary> Возвращает сводку оплат, требующих внимания администратора. </summary>
    Task<AdminBillingAttentionDto> GetAttentionAsync(int? periodDays = null);

    /// <summary> Возвращает список покупок для административного отчёта. </summary>
    Task<IReadOnlyList<AdminBillingPurchaseDto>> GetPurchasesAsync();

    /// <summary> Возвращает доступные тарифные планы. </summary>
    Task<IReadOnlyList<AdminBillingAvailablePlanDto>> GetAvailablePlansAsync();

    /// <summary> Возвращает платёж по идентификатору. </summary>
    Task<AdminBillingPaymentDto?> GetPaymentAsync(int paymentId);

    /// <summary> Возвращает административную сводку биллинга. </summary>
    Task<AdminBillingDto> GetAsync();

    /// <summary> Возвращает страницу платежей по запросу. </summary>
    Task<AdminBillingPaymentsPageDto> GetPaymentsPageAsync(AdminBillingPageQuery query);

    /// <summary> Возвращает страницу подписок организаций по запросу. </summary>
    Task<AdminBillingOrganizationSubscriptionsPageDto> GetOrganizationSubscriptionsPageAsync(AdminBillingPageQuery query);

    /// <summary> Возвращает аудит статусов платежа. </summary>
    Task<IReadOnlyList<AdminPaymentStatusAuditDto>> GetPaymentStatusAuditsAsync(int paymentId);

    /// <summary> Возвращает аудит подписки в заданной области. </summary>
    Task<IReadOnlyList<AdminSubscriptionAuditDto>> GetSubscriptionAuditsAsync(string scope, int subscriptionId);
}
