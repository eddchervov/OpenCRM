using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Subscriptions.Dtos;

namespace OpenCRM.Modules.Billing;

/// <summary> Сервис правил покупки тарифов организациями. </summary>
internal sealed class OrganizationSubscriptionPurchasePolicyService(
    OpenCRMContext context,
    IFeatureDefaultsInitializer featureDefaultsInitializer,
    IOrganizationPermissionService organizationPermissionService) : IOrganizationSubscriptionPurchasePolicyService
{
    /// <summary> Код источника промо-подписки. </summary>
    private const string PromoSourceCode = "promo";

    /// <summary> Проверяет, может ли организация купить выбранный тариф. </summary>
    public async Task<SubscriptionResult> CanPurchasePlanAsync(string userId, Guid organizationId, int planId)
    {
        await featureDefaultsInitializer.EnsureDefaultsAsync();

        var organizationIsManageable = await context.Organizations
            .AsNoTracking()
            .AnyAsync(organization => organization.Id == organizationId
                && organization.IsActive
                && !organization.IsDeleted
                && !organization.IsArchived
                && organization.Members.Any(member => member.UserId == userId));

        if (!organizationIsManageable)
        {
            throw new InvalidOperationException("Организация не найдена или доступ закрыт.");
        }

        if (!await organizationPermissionService.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.BillingManage))
        {
            throw new InvalidOperationException(OrganizationPermissionCatalog.BuildDeniedMessage(OrganizationPermissionCodes.BillingManage));
        }

        var plan = await context.SubscriptionPlans
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == planId);

        if (plan == null || !plan.IsActive)
        {
            return SubscriptionResult.CreateFailed("Бизнес-тариф не найден или недоступен.");
        }

        if (SubscriptionPlanCatalogCodes.NormalizePlanType(plan.PlanTypeCode) != SubscriptionPlanCatalogCodes.PlanTypes.Business)
        {
            return SubscriptionResult.CreateFailed("Этот тариф не относится к рабочим пространствам.");
        }

        if (plan.IsDefaultPlan || plan.Group == SubscriptionPlanCatalogCodes.OrganizationFreeGroup)
        {
            return SubscriptionResult.CreateFailed("Базовый тариф нельзя подключить вручную.");
        }

        if (plan.IsDemoPlan)
        {
            return SubscriptionResult.CreateFailed("Демо-тариф нельзя оплачивать вручную.");
        }

        var currentSubscription = await context.OrganizationSubscriptions
            .AsNoTracking()
            .Include(subscription => subscription.SubscriptionPlan)
            .Where(subscription => subscription.OrganizationId == organizationId && subscription.EndDate >= DateTime.UtcNow)
            .OrderByDescending(subscription => subscription.EndDate)
            .FirstOrDefaultAsync();

        if (currentSubscription == null
            || currentSubscription.SubscriptionPlan.IsDemoPlan
            || currentSubscription.SubscriptionPlan.IsDefaultPlan)
        {
            return SubscriptionResult.CreateSuccess();
        }

        // Подарочный доступ не мешает покупке: оставшиеся подарочные дни добавятся к оплаченному сроку.
        if (currentSubscription.SourceCode == PromoSourceCode)
        {
            return SubscriptionResult.CreateSuccess();
        }

        if (currentSubscription.SubscriptionPlanId == planId)
        {
            return SubscriptionResult.CreateFailed("Этот тариф уже активен у рабочего пространства.");
        }

        return SubscriptionResult.CreateFailed(
            $"Сменить тариф можно после окончания текущего: {currentSubscription.EndDate:dd.MM.yyyy}. "
            + "Продление текущего тарифа откроется за 7 дней до окончания.");
    }
}
