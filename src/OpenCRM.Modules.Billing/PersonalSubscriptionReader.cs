using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Services.Interfaces.Subscriptions.Dtos;

namespace OpenCRM.Modules.Billing;

/// <summary> Читатель текущих личных подписок из базы. </summary>
internal sealed class PersonalSubscriptionReader(
    OpenCRMContext context,
    IFeatureDefaultsInitializer featureDefaultsInitializer,
    IPlatformCommercialPolicy? platformCommercialPolicy = null)
{
    /// <summary> Возвращает текущую подписку пользователя. </summary>
    public async Task<UserSubscriptionDto?> GetCurrentAsync(string userId)
    {
        if (platformCommercialPolicy?.CommercialEnabled != true)
        {
            return null;
        }

        await featureDefaultsInitializer.EnsureDefaultsAsync();

        var now = DateTime.UtcNow;
        var subscription = await context.UserSubscriptions
            .AsNoTracking()
            .Include(userSubscription => userSubscription.SubscriptionPlan)
                .ThenInclude(plan => plan.Features)
                    .ThenInclude(feature => feature.FeatureDefinition)
            .FirstOrDefaultAsync(userSubscription =>
                userSubscription.UserId == userId && userSubscription.EndDate >= DateTime.UtcNow);

        if (subscription != null)
        {
            var effectivePurchase = await SubscriptionPurchaseSnapshotResolver.GetForUserAsync(context, userId,
                subscription.SubscriptionPlanId, subscription.SubscriptionPurchaseId, subscription.SourceCode, now);
            if (effectivePurchase != null)
            {
                subscription.SubscriptionPlan = SubscriptionPlanSnapshot.Restore(effectivePurchase.PlanSnapshot);
            }
        }

        return PersonalSubscriptionMapper.MapUserSubscription(subscription);
    }

    /// <summary> Проверяет доступность демо-подписки для пользователя. </summary>
    public async Task<(bool Available, string Message)> CheckDemoAvailabilityAsync(string userId)
    {
        if (platformCommercialPolicy?.CommercialEnabled != true)
        {
            return (false, "Коммерческий режим выключен.");
        }

        var currentSubscription = await context.UserSubscriptions
            .AsNoTracking()
            .Include(subscription => subscription.SubscriptionPlan)
            .FirstOrDefaultAsync(subscription =>
                subscription.UserId == userId && subscription.EndDate >= DateTime.UtcNow);

        if (currentSubscription?.SubscriptionPlan.IsDemoPlan == true)
        {
            return (true, "Демо-тариф уже активен.");
        }

        if (currentSubscription != null)
        {
            return (false, "У вас уже есть активная платная подписка.");
        }

        var hasPaidHistory = await context.UserSubscriptionHistory
            .AnyAsync(history => history.UserId == userId && !history.SubscriptionPlan.IsDemoPlan);

        return hasPaidHistory
            ? (false, "Демо-тариф доступен только тем, кто еще не покупал платные тарифы.")
            : (true, "Демо-тариф доступен.");
    }
}
