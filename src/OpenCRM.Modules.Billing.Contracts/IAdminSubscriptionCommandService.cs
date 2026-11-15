using OpenCRM.Modules.Billing.Contracts.Dtos;

namespace OpenCRM.Modules.Billing.Contracts;

/// <summary>Выполняет административные изменения подписок без создания оплаты.</summary>
public interface IAdminSubscriptionCommandService
{
    /// <summary> Выдаёт пользователю тариф на указанное число дней. </summary>
    Task<AdminBillingSubscriptionOperationDto> GrantUserPlanAsync(
        string userId,
        int planId,
        int days,
        string? reason,
        string adminUserId);

    /// <summary> Выдаёт пользователю демо-тариф. </summary>
    Task<AdminBillingSubscriptionOperationDto> GrantUserDemoAsync(string userId, string? reason, string adminUserId);

    /// <summary> Отзывает демо-тариф пользователя. </summary>
    Task<AdminBillingSubscriptionOperationDto> RevokeUserDemoAsync(string userId, string? reason, string adminUserId);

    /// <summary> Выдаёт организации тариф на указанное число дней. </summary>
    Task<AdminBillingSubscriptionOperationDto> GrantOrganizationPlanAsync(
        Guid organizationId,
        int planId,
        int days,
        string? reason,
        string adminUserId);

    /// <summary> Выдаёт организации демо-тариф. </summary>
    Task<AdminBillingSubscriptionOperationDto> GrantOrganizationDemoAsync(
        string userId,
        Guid organizationId,
        string adminUserId,
        string? reason);

    /// <summary> Отзывает демо-тариф организации. </summary>
    Task<AdminBillingSubscriptionOperationDto> RevokeOrganizationDemoAsync(
        string userId,
        Guid organizationId,
        string adminUserId,
        string? reason);

    /// <summary> Останавливает подписку в заданной области. </summary>
    Task<AdminBillingSubscriptionOperationDto> StopSubscriptionAsync(
        string subscriptionScope,
        int subscriptionId,
        string? reason,
        DateTime? expectedEndDate,
        string adminUserId);

    /// <summary> Продлевает подписку на указанное число дней. </summary>
    Task<AdminBillingSubscriptionOperationDto> ExtendSubscriptionAsync(
        string subscriptionScope,
        int subscriptionId,
        int days,
        string? reason,
        DateTime? expectedEndDate,
        string adminUserId);
}
