using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Billing.Contracts.Dtos;

namespace OpenCRM.Modules.Billing;

/// <summary> Фасад административных команд по подпискам. </summary>
internal sealed class AdminSubscriptionCommandService(
    AdminPersonalSubscriptionCommandService personal,
    AdminOrganizationSubscriptionCommandService organizations,
    AdminSubscriptionAdjustmentService adjustments) : IAdminSubscriptionCommandService
{
    /// <summary> Выдаёт пользователю тариф на заданное число дней. </summary>
    public Task<AdminBillingSubscriptionOperationDto> GrantUserPlanAsync(
        string userId,
        int planId,
        int days,
        string? reason,
        string adminUserId) =>
        personal.GrantUserPlanAsync(userId, planId, days, reason, adminUserId);

    /// <summary> Выдаёт пользователю демо-подписку. </summary>
    public Task<AdminBillingSubscriptionOperationDto> GrantUserDemoAsync(string userId, string? reason, string adminUserId) =>
        personal.GrantUserDemoAsync(userId, reason, adminUserId);

    /// <summary> Отзывает демо-подписку пользователя. </summary>
    public Task<AdminBillingSubscriptionOperationDto> RevokeUserDemoAsync(string userId, string? reason, string adminUserId) =>
        personal.RevokeUserDemoAsync(userId, reason, adminUserId);

    /// <summary> Выдаёт организации тариф на заданное число дней. </summary>
    public Task<AdminBillingSubscriptionOperationDto> GrantOrganizationPlanAsync(
        Guid organizationId,
        int planId,
        int days,
        string? reason,
        string adminUserId) =>
        organizations.GrantOrganizationPlanAsync(organizationId, planId, days, reason, adminUserId);

    /// <summary> Выдаёт организации демо-доступ. </summary>
    public Task<AdminBillingSubscriptionOperationDto> GrantOrganizationDemoAsync(
        string userId,
        Guid organizationId,
        string adminUserId,
        string? reason) =>
        organizations.GrantOrganizationDemoAsync(userId, organizationId, adminUserId, reason);

    /// <summary> Отзывает демо-доступ организации. </summary>
    public Task<AdminBillingSubscriptionOperationDto> RevokeOrganizationDemoAsync(
        string userId,
        Guid organizationId,
        string adminUserId,
        string? reason) =>
        organizations.RevokeOrganizationDemoAsync(userId, organizationId, adminUserId, reason);

    /// <summary> Останавливает подписку. </summary>
    public Task<AdminBillingSubscriptionOperationDto> StopSubscriptionAsync(
        string subscriptionScope,
        int subscriptionId,
        string? reason,
        DateTime? expectedEndDate,
        string adminUserId) =>
        adjustments.StopSubscriptionAsync(subscriptionScope, subscriptionId, reason, expectedEndDate, adminUserId);

    /// <summary> Продлевает подписку на заданное число дней. </summary>
    public Task<AdminBillingSubscriptionOperationDto> ExtendSubscriptionAsync(
        string subscriptionScope,
        int subscriptionId,
        int days,
        string? reason,
        DateTime? expectedEndDate,
        string adminUserId) =>
        adjustments.ExtendSubscriptionAsync(subscriptionScope, subscriptionId, days, reason, expectedEndDate, adminUserId);
}
