using Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Services.Interfaces.Features;
using OpenCRM.Services.Interfaces.Subscriptions.Dtos;

namespace OpenCRM.Modules.Billing;

/// <summary> Сервис запросов о личных подписках. </summary>
internal sealed class PersonalSubscriptionQueryService(
    OpenCRMContext context,
    ILogger<PersonalSubscriptionQueryService> logger,
    IFeatureDefaultsInitializer featureDefaultsInitializer,
    IFeatureAccessService featureAccessService,
    PersonalSubscriptionReader reader,
    IPersonalSubscriptionCatalogQueryService catalog,
    IPlatformCommercialPolicy? platformCommercialPolicy = null) : IPersonalSubscriptionQueryService
{
    /// <summary> Возвращает текущую подписку пользователя. </summary>
    public async Task<UserSubscriptionDto?> GetUserSubscriptionAsync(string userId)
    {
        return await reader.GetCurrentAsync(userId);
    }

    /// <summary> Возвращает объединённую историю подписок пользователя. </summary>
    public async Task<IEnumerable<SubscriptionHistoryDto>> GetCombinedSubscriptionHistoryAsync(string userId)
    {
        if (platformCommercialPolicy?.CommercialEnabled != true)
        {
            return [];
        }

        var history = await context.UserSubscriptionHistory
            .AsNoTracking()
            .Include(subscriptionHistory => subscriptionHistory.SubscriptionPlan)
                .ThenInclude(plan => plan.Features)
                    .ThenInclude(feature => feature.FeatureDefinition)
            .Where(subscriptionHistory => subscriptionHistory.UserId == userId)
            .OrderByDescending(subscriptionHistory => subscriptionHistory.CreatedAt)
            .ToListAsync();

        return history.Select(PersonalSubscriptionMapper.MapHistory).ToList();
    }

    /// <summary> Проверяет наличие активной подписки у пользователя. </summary>
    public async Task<bool> HasActiveSubscriptionAsync(string userId)
    {
        var subscription = await reader.GetCurrentAsync(userId);

        return subscription != null;
    }

    /// <summary> Возвращает лимит техники пользователя. </summary>
    public async Task<int> GetUserVehicleLimitAsync(string userId)
    {
        return await featureAccessService.GetIntValueAsync(userId, FeatureCodes.VehiclesLimit, PlatformDefaults.VehicleLimit);
    }

    /// <summary> Возвращает следующий тариф пользователя. </summary>
    public async Task<SubscriptionPlanDto?> GetNextPlanAsync(string userId)
    {
        if (platformCommercialPolicy?.CommercialEnabled != true)
        {
            return null;
        }

        var currentSubscription = await reader.GetCurrentAsync(userId);
        var availablePlans = await catalog.GetAvailablePlansAsync();

        if (!availablePlans.Any())
        {
            return null;
        }

        if (currentSubscription == null)
        {
            return availablePlans.FirstOrDefault();
        }

        return availablePlans
            .Where(plan => plan.Order > currentSubscription.SubscriptionPlan.Order)
            .OrderBy(plan => plan.Order)
            .FirstOrDefault();
    }

    /// <summary> Проверяет, находится ли пользователь на максимальном тарифе. </summary>
    public async Task<bool> IsMaxPlanAsync(string userId)
    {
        if (platformCommercialPolicy?.CommercialEnabled != true)
        {
            return false;
        }

        var currentSubscription = await reader.GetCurrentAsync(userId);
        if (currentSubscription == null)
        {
            return false;
        }

        var availablePlans = await catalog.GetAvailablePlansAsync();
        var maxOrder = availablePlans.Max(plan => plan.Order);

        return currentSubscription.SubscriptionPlan.Order >= maxOrder;
    }

    /// <summary> Возвращает информацию о продлении подписки пользователя. </summary>
    public async Task<(bool CanExtend, int DaysUntilExpiry, string Message)> GetSubscriptionExtensionInfoAsync(string userId)
    {
        var currentSubscription = await reader.GetCurrentAsync(userId);

        if (currentSubscription == null)
        {
            return (false, 0, "У вас нет активной подписки");
        }

        if (currentSubscription.SubscriptionPlan.IsDemoPlan)
        {
            var demoDaysUntilExpiry = (int)Math.Ceiling((currentSubscription.EndDate - DateTime.UtcNow).TotalDays);
            return (false, demoDaysUntilExpiry, "Демо-тариф нельзя продлить");
        }

        var daysUntilExpiry = (int)Math.Ceiling((currentSubscription.EndDate - DateTime.UtcNow).TotalDays);
        var canExtend = daysUntilExpiry <= 7;

        var message = canExtend
            ? $"Доступно для продления. Осталось дней: {daysUntilExpiry}"
            : $"Продление будет доступно за 7 дней до окончания. Осталось дней: {daysUntilExpiry}";

        return (canExtend, daysUntilExpiry, message);
    }

    /// <summary> Проверяет возможность продления подписки пользователя. </summary>
    public async Task<bool> CanExtendSubscriptionAsync(string userId)
    {
        var currentSubscription = await reader.GetCurrentAsync(userId);

        if (currentSubscription == null || currentSubscription.SubscriptionPlan.IsDemoPlan)
        {
            return false;
        }

        return (currentSubscription.EndDate - DateTime.UtcNow).TotalDays <= 7;
    }

    /// <summary> Проверяет доступность демо-подписки для пользователя. </summary>
    public Task<(bool Available, string Message)> CheckDemoSubscriptionAvailabilityAsync(string userId)
    {
        return reader.CheckDemoAvailabilityAsync(userId);
    }

    /// <summary> Обеспечивает пользователю доступ к тарифу по умолчанию. </summary>
    public async Task EnsureDefaultPlanAccessAsync(string userId)
    {
        if (platformCommercialPolicy?.CommercialEnabled != true || string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        await featureDefaultsInitializer.EnsureDefaultsAsync();

        var hasActiveSubscription = await context.UserSubscriptions
            .AnyAsync(subscription => subscription.UserId == userId && subscription.EndDate >= DateTime.UtcNow);

        if (hasActiveSubscription)
        {
            return;
        }

        var defaultPlanExists = await context.SubscriptionPlans
            .AnyAsync(plan =>
                plan.IsActive
                && plan.IsDefaultPlan
                && plan.PlanTypeCode == SubscriptionPlanCatalogCodes.PlanTypes.Personal);

        if (!defaultPlanExists)
        {
            logger.LogWarning(
                "Для пользователя {UserId} не удалось подхватить базовый тариф: активный тариф по умолчанию не найден.",
                userId);
            return;
        }

        await featureAccessService.GetSnapshotAsync(userId);
    }
}
