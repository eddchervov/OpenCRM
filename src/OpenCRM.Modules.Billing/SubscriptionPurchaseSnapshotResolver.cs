using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;

namespace OpenCRM.Modules.Billing;

/// <summary> Находит оплаченный снимок тарифа, действующий в выбранный момент. </summary>
internal static class SubscriptionPurchaseSnapshotResolver
{
    public static async Task<SubscriptionPurchase?> GetForUserAsync(
        OpenCRMContext context,
        string userId,
        int subscriptionPlanId,
        Guid? currentPurchaseId,
        string sourceCode,
        DateTime now)
    {
        if (sourceCode != "paid" || !currentPurchaseId.HasValue
            || !await context.SubscriptionPurchases.AnyAsync(purchase => purchase.Id == currentPurchaseId
                && purchase.UserId == userId
                && purchase.OrganizationId == null
                && purchase.AppliedAt != null
                && purchase.CreationOperation.SubscriptionPlanId == subscriptionPlanId))
        {
            return null;
        }

        return await context.SubscriptionPurchases
            .AsNoTracking()
            .Where(purchase => purchase.UserId == userId
                && purchase.OrganizationId == null
                && purchase.AppliedAt != null
                && purchase.GrantedFrom <= now
                && purchase.GrantedUntil >= now
                && purchase.CreationOperation.SubscriptionPlanId == subscriptionPlanId)
            .OrderByDescending(purchase => purchase.GrantedFrom)
            .FirstOrDefaultAsync();
    }

    public static async Task<SubscriptionPurchase?> GetForOrganizationAsync(
        OpenCRMContext context,
        Guid organizationId,
        int subscriptionPlanId,
        Guid? currentPurchaseId,
        string sourceCode,
        DateTime now)
    {
        if (sourceCode != "paid" || !currentPurchaseId.HasValue
            || !await context.SubscriptionPurchases.AnyAsync(purchase => purchase.Id == currentPurchaseId
                && purchase.OrganizationId == organizationId
                && purchase.AppliedAt != null
                && purchase.CreationOperation.SubscriptionPlanId == subscriptionPlanId))
        {
            return null;
        }

        return await context.SubscriptionPurchases
            .AsNoTracking()
            .Where(purchase => purchase.OrganizationId == organizationId
                && purchase.AppliedAt != null
                && purchase.GrantedFrom <= now
                && purchase.GrantedUntil >= now
                && purchase.CreationOperation.SubscriptionPlanId == subscriptionPlanId)
            .OrderByDescending(purchase => purchase.GrantedFrom)
            .FirstOrDefaultAsync();
    }
}
