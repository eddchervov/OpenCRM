using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Modules.Billing.Features;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Features.Dtos;
using OpenCRM.Services.Interfaces.Subscriptions;
using OpenCRM.Services.Interfaces.Subscriptions.Dtos.Admin;

namespace OpenCRM.Modules.Billing.Subscriptions;

/// <summary> Сервис администрирования тарифов подписок. </summary>
public class SubscriptionPlanAdminService : ISubscriptionPlanAdminService
{
    /// <summary> Контекст базы данных для работы с тарифами. </summary>
    private readonly OpenCRMContext _context;

    /// <summary> Создаёт сервис с контекстом базы данных. </summary>
    public SubscriptionPlanAdminService(OpenCRMContext context)
    {
        _context = context;
    }

    /// <summary> Возвращает все тарифы подписок. </summary>
    public async Task<List<SubscriptionPlanAdminDto>> GetAllSubscriptionPlansAsync()
    {
        await FeatureBootstrapper.EnsureDefaultsAsync(_context);

        var plans = await _context.SubscriptionPlans
            .Include(sp => sp.Features)
                .ThenInclude(f => f.FeatureDefinition)
            .OrderBy(sp => sp.PlanTypeCode)
            .ThenBy(sp => sp.Order)
            .ThenBy(sp => sp.Name)
            .ToListAsync();

        var usageLookup = await BuildUsageLookupAsync(plans.Select(x => x.Id).ToList());
        return plans.Select(plan => MapToAdminDto(plan, usageLookup.Contains(plan.Id))).ToList();
    }

    /// <summary> Возвращает тариф по его идентификатору. </summary>
    public async Task<SubscriptionPlanAdminDto?> GetSubscriptionPlanByIdAsync(int id)
    {
        await FeatureBootstrapper.EnsureDefaultsAsync(_context);

        var plan = await _context.SubscriptionPlans
            .Include(sp => sp.Features)
                .ThenInclude(f => f.FeatureDefinition)
            .FirstOrDefaultAsync(sp => sp.Id == id);

        if (plan == null)
        {
            return null;
        }

        var hasUsageHistory = await HasUsageHistoryAsync(plan.Id);
        return MapToAdminDto(plan, hasUsageHistory);
    }

    /// <summary> Создаёт новый тариф подписки. </summary>
    public async Task<SubscriptionPlanAdminDto> CreateSubscriptionPlanAsync(SubscriptionPlanCreateDto createDto)
    {
        await FeatureBootstrapper.EnsureDefaultsAsync(_context);

        if (createDto.Group == SubscriptionPlanCatalogCodes.OrganizationFreeGroup)
        {
            throw new InvalidOperationException("Системная группа бесплатного тарифа зарезервирована.");
        }

        var planTypeCode = SubscriptionPlanCatalogCodes.NormalizePlanType(createDto.PlanTypeCode);
        EnsurePlanFlagsCompatibility(createDto.IsDefaultPlan, createDto.IsDemoPlan);
        var subscriptionPlan = new SubscriptionPlan
        {
            Name = createDto.Name,
            Description = createDto.Description,
            Price = createDto.Price,
            DiscountPrice = NormalizeDiscountPrice(createDto.Price, createDto.DiscountPrice),
            DurationDays = createDto.DurationDays,
            VehicleLimit = createDto.VehicleLimit,
            Order = createDto.Order,
            Group = createDto.Group,
            PlanTypeCode = planTypeCode,
            IsActive = createDto.IsActive,
            IsDefaultPlan = createDto.IsDefaultPlan,
            IsDemoPlan = createDto.IsDemoPlan
        };

        if (createDto.IsDefaultPlan)
        {
            await ClearDefaultFlagAsync(null, planTypeCode);
            subscriptionPlan.IsActive = true;
        }

        if (createDto.IsDemoPlan)
        {
            await ClearDemoFlagAsync(null, planTypeCode);
            subscriptionPlan.IsActive = true;
        }

        _context.SubscriptionPlans.Add(subscriptionPlan);
        await _context.SaveChangesAsync();
        await SyncVehiclesLimitFeatureAsync(subscriptionPlan.Id, createDto.VehicleLimit);

        return MapToAdminDto(subscriptionPlan, false);
    }

    /// <summary> Обновляет тариф подписки. </summary>
    public async Task<SubscriptionPlanAdminDto?> UpdateSubscriptionPlanAsync(int id, SubscriptionPlanUpdateDto updateDto)
    {
        await FeatureBootstrapper.EnsureDefaultsAsync(_context);

        var subscriptionPlan = await _context.SubscriptionPlans.FindAsync(id);
        if (subscriptionPlan == null)
        {
            return null;
        }

        if (SubscriptionPlanAdminService.HasInvalidFreePlanSettings(subscriptionPlan, updateDto))
        {
            throw new InvalidOperationException("Нельзя изменить системную идентичность, стоимость или доступность бесплатного тарифа.");
        }

        var planTypeCode = SubscriptionPlanCatalogCodes.NormalizePlanType(updateDto.PlanTypeCode);
        EnsurePlanFlagsCompatibility(updateDto.IsDefaultPlan, updateDto.IsDemoPlan);

        subscriptionPlan.Name = updateDto.Name;
        subscriptionPlan.Description = updateDto.Description;
        subscriptionPlan.Price = updateDto.Price;
        subscriptionPlan.DiscountPrice = NormalizeDiscountPrice(updateDto.Price, updateDto.DiscountPrice);
        subscriptionPlan.DurationDays = updateDto.DurationDays;
        subscriptionPlan.VehicleLimit = updateDto.VehicleLimit;
        subscriptionPlan.Order = updateDto.Order;
        subscriptionPlan.Group = updateDto.Group;
        subscriptionPlan.PlanTypeCode = planTypeCode;

        if (subscriptionPlan.IsDefaultPlan && !updateDto.IsDefaultPlan)
        {
            throw new InvalidOperationException("Нельзя снять признак тарифа по умолчанию, пока не назначен другой базовый тариф.");
        }

        subscriptionPlan.IsDefaultPlan = updateDto.IsDefaultPlan;
        subscriptionPlan.IsDemoPlan = updateDto.IsDemoPlan;

        if (updateDto.IsDefaultPlan)
        {
            await ClearDefaultFlagAsync(id, planTypeCode);
            subscriptionPlan.IsActive = true;
        }

        if (updateDto.IsDemoPlan)
        {
            await ClearDemoFlagAsync(id, planTypeCode);
            subscriptionPlan.IsActive = true;
        }

        if (!updateDto.IsDefaultPlan && !updateDto.IsDemoPlan)
        {
            subscriptionPlan.IsActive = updateDto.IsActive;
        }

        await _context.SaveChangesAsync();
        await SyncVehiclesLimitFeatureAsync(subscriptionPlan.Id, updateDto.VehicleLimit);

        var hasUsageHistory = await HasUsageHistoryAsync(subscriptionPlan.Id);
        return MapToAdminDto(subscriptionPlan, hasUsageHistory);
    }

    /// <summary> Переключает статус активности тарифа. </summary>
    public async Task<bool> ToggleSubscriptionPlanStatusAsync(int id)
    {
        await FeatureBootstrapper.EnsureDefaultsAsync(_context);

        var subscriptionPlan = await _context.SubscriptionPlans.FindAsync(id);
        if (subscriptionPlan == null)
        {
            return false;
        }

        if (subscriptionPlan.IsDefaultPlan || subscriptionPlan.Group == SubscriptionPlanCatalogCodes.OrganizationFreeGroup)
        {
            return false;
        }

        if (subscriptionPlan.PlanTypeCode == SubscriptionPlanCatalogCodes.PlanTypes.Personal)
        {
            var activeNonDefaultPlansCount = await _context.SubscriptionPlans.CountAsync(x =>
                x.IsActive &&
                !x.IsDefaultPlan &&
                x.PlanTypeCode == subscriptionPlan.PlanTypeCode);

            if (subscriptionPlan.IsActive && activeNonDefaultPlansCount <= 1)
            {
                return false;
            }
        }

        subscriptionPlan.IsActive = !subscriptionPlan.IsActive;
        await _context.SaveChangesAsync();

        return true;
    }

    /// <summary> Активирует тариф подписки. </summary>
    public async Task<bool> ActivateSubscriptionPlanAsync(int id)
    {
        await FeatureBootstrapper.EnsureDefaultsAsync(_context);

        var subscriptionPlan = await _context.SubscriptionPlans.FindAsync(id);
        if (subscriptionPlan == null)
        {
            return false;
        }

        subscriptionPlan.IsActive = true;
        await _context.SaveChangesAsync();

        return true;
    }

    /// <summary> Деактивирует тариф подписки. </summary>
    public async Task<bool> DeactivateSubscriptionPlanAsync(int id)
    {
        await FeatureBootstrapper.EnsureDefaultsAsync(_context);

        var subscriptionPlan = await _context.SubscriptionPlans.FindAsync(id);
        if (subscriptionPlan == null)
        {
            return false;
        }

        if (subscriptionPlan.IsDefaultPlan || subscriptionPlan.Group == SubscriptionPlanCatalogCodes.OrganizationFreeGroup)
        {
            return false;
        }

        if (subscriptionPlan.PlanTypeCode == SubscriptionPlanCatalogCodes.PlanTypes.Personal)
        {
            var activeNonDefaultPlansCount = await _context.SubscriptionPlans.CountAsync(x =>
                x.IsActive &&
                !x.IsDefaultPlan &&
                x.PlanTypeCode == subscriptionPlan.PlanTypeCode);

            if (subscriptionPlan.IsActive && activeNonDefaultPlansCount <= 1)
            {
                return false;
            }
        }

        subscriptionPlan.IsActive = false;
        await _context.SaveChangesAsync();

        return true;
    }

    /// <summary> Удаляет тариф подписки. </summary>
    public async Task<bool> DeleteSubscriptionPlanAsync(int id)
    {
        await FeatureBootstrapper.EnsureDefaultsAsync(_context);

        var subscriptionPlan = await _context.SubscriptionPlans
            .Include(x => x.Features)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (subscriptionPlan == null)
        {
            return false;
        }

        if (subscriptionPlan.IsDefaultPlan || subscriptionPlan.Group == SubscriptionPlanCatalogCodes.OrganizationFreeGroup
            || await HasUsageHistoryAsync(subscriptionPlan.Id))
        {
            return false;
        }

        _context.SubscriptionPlanFeatures.RemoveRange(subscriptionPlan.Features);
        _context.SubscriptionPlans.Remove(subscriptionPlan);
        await _context.SaveChangesAsync();

        return true;
    }

    /// <summary> Защищает системную идентичность и бессрочную бесплатную доступность Free. </summary>
    private static bool HasInvalidFreePlanSettings(SubscriptionPlan plan, SubscriptionPlanUpdateDto update)
    {
        if (plan.Group != SubscriptionPlanCatalogCodes.OrganizationFreeGroup)
        {
            return update.Group == SubscriptionPlanCatalogCodes.OrganizationFreeGroup;
        }

        return update.Group != SubscriptionPlanCatalogCodes.OrganizationFreeGroup
            || update.PlanTypeCode != SubscriptionPlanCatalogCodes.PlanTypes.Business
            || update.IsDefaultPlan || update.IsDemoPlan || !update.IsActive
            || update.Price != 0 || update.DiscountPrice.HasValue;
    }

    /// <summary> Синхронизирует функцию лимита техники тарифа. </summary>
    private async Task SyncVehiclesLimitFeatureAsync(int planId, int vehicleLimit)
    {
        var planTypeCode = await _context.SubscriptionPlans
            .Where(x => x.Id == planId)
            .Select(x => x.PlanTypeCode)
            .FirstAsync();

        var limitFeatureCode = SubscriptionPlanCatalogCodes.NormalizePlanType(planTypeCode) == SubscriptionPlanCatalogCodes.PlanTypes.Business
            ? Helpers.FeatureCodes.WorkspaceVehiclesLimit
            : Helpers.FeatureCodes.VehiclesLimit;

        var feature = await _context.FeatureDefinitions.FirstAsync(x => x.Code == limitFeatureCode);

        var assignment = await _context.SubscriptionPlanFeatures
            .FirstOrDefaultAsync(x => x.SubscriptionPlanId == planId && x.FeatureDefinitionId == feature.Id);

        if (assignment == null)
        {
            assignment = new SubscriptionPlanFeature
            {
                SubscriptionPlanId = planId,
                FeatureDefinitionId = feature.Id
            };
            _context.SubscriptionPlanFeatures.Add(assignment);
        }

        assignment.IntValue = vehicleLimit;
        assignment.BoolValue = null;

        await _context.SaveChangesAsync();
    }

    /// <summary> Преобразует тариф в административный DTO. </summary>
    private static SubscriptionPlanAdminDto MapToAdminDto(SubscriptionPlan subscriptionPlan, bool hasUsageHistory)
    {
        return new SubscriptionPlanAdminDto
        {
            Id = subscriptionPlan.Id,
            Name = subscriptionPlan.Name,
            Description = subscriptionPlan.Description,
            Price = subscriptionPlan.Price,
            DiscountPrice = subscriptionPlan.DiscountPrice,
            EffectivePrice = subscriptionPlan.EffectivePrice,
            HasDiscount = subscriptionPlan.HasDiscount,
            DurationDays = subscriptionPlan.DurationDays,
            IsActive = subscriptionPlan.IsActive,
            VehicleLimit = subscriptionPlan.VehicleLimit,
            Order = subscriptionPlan.Order,
            Group = subscriptionPlan.Group,
            PlanTypeCode = SubscriptionPlanCatalogCodes.NormalizePlanType(subscriptionPlan.PlanTypeCode),
            PlanTypeName = SubscriptionPlanCatalogCodes.GetPlanTypeName(subscriptionPlan.PlanTypeCode),
            IsDemoPlan = subscriptionPlan.IsDemoPlan,
            IsDefaultPlan = subscriptionPlan.IsDefaultPlan,
            HasUsageHistory = hasUsageHistory,
            CanDelete = !subscriptionPlan.IsDefaultPlan
                && subscriptionPlan.Group != SubscriptionPlanCatalogCodes.OrganizationFreeGroup
                && !hasUsageHistory,
            Features = subscriptionPlan.Features
                .OrderBy(x => x.FeatureDefinition.Name)
                .Select(x => new FeatureValueDto
                {
                    FeatureId = x.FeatureDefinitionId,
                    Code = x.FeatureDefinition.Code,
                    Name = x.FeatureDefinition.Name,
                    Category = x.FeatureDefinition.Category,
                    ScopeCode = x.FeatureDefinition.ScopeCode,
                    ScopeName = FeatureCatalogCodes.Scopes.GetName(x.FeatureDefinition.ScopeCode),
                    AudienceCode = x.FeatureDefinition.AudienceCode,
                    AudienceName = FeatureCatalogCodes.Audiences.GetName(x.FeatureDefinition.AudienceCode),
                    ValueType = x.FeatureDefinition.ValueType.ToString(),
                    BoolValue = x.BoolValue,
                    IntValue = x.IntValue
                })
                .ToList()
        };
    }

    /// <summary> Снимает флаг тарифа по умолчанию у остальных тарифов типа. </summary>
    private async Task ClearDefaultFlagAsync(int? exceptPlanId, string planTypeCode)
    {
        var currentDefaults = await _context.SubscriptionPlans
            .Where(x =>
                x.IsDefaultPlan &&
                x.PlanTypeCode == planTypeCode &&
                (!exceptPlanId.HasValue || x.Id != exceptPlanId.Value))
            .ToListAsync();

        foreach (var plan in currentDefaults)
        {
            plan.IsDefaultPlan = false;
        }
    }

    /// <summary> Снимает флаг демо-тарифа у остальных тарифов типа. </summary>
    private async Task ClearDemoFlagAsync(int? exceptPlanId, string planTypeCode)
    {
        var currentDemoPlans = await _context.SubscriptionPlans
            .Where(x =>
                x.IsDemoPlan &&
                x.PlanTypeCode == planTypeCode &&
                (!exceptPlanId.HasValue || x.Id != exceptPlanId.Value))
            .ToListAsync();

        foreach (var plan in currentDemoPlans)
        {
            plan.IsDemoPlan = false;
        }
    }

    /// <summary> Проверяет совместимость флагов тарифа по умолчанию и демо. </summary>
    private static void EnsurePlanFlagsCompatibility(bool isDefaultPlan, bool isDemoPlan)
    {
        if (isDefaultPlan && isDemoPlan)
        {
            throw new InvalidOperationException("Тариф не может одновременно быть базовым и демо.");
        }
    }

    /// <summary> Согласует цену со скидкой, при необходимости обнуляя её. </summary>
    private static decimal? NormalizeDiscountPrice(decimal price, decimal? discountPrice)
    {
        if (!discountPrice.HasValue)
        {
            return null;
        }

        return discountPrice.Value >= 0 && discountPrice.Value < price
            ? discountPrice.Value
            : null;
    }

    /// <summary> Проверяет наличие истории использования тарифа. </summary>
    private async Task<bool> HasUsageHistoryAsync(int subscriptionPlanId)
    {
        return await _context.UserSubscriptions.AnyAsync(x => x.SubscriptionPlanId == subscriptionPlanId)
            || await _context.UserSubscriptionHistory.AnyAsync(x => x.SubscriptionPlanId == subscriptionPlanId)
            || await _context.SubscriptionPayments.AnyAsync(x => x.SubscriptionPlanId == subscriptionPlanId)
            || await _context.OrganizationSubscriptions.AnyAsync(x => x.SubscriptionPlanId == subscriptionPlanId);
    }

    /// <summary> Строит множество тарифов с историей использования. </summary>
    private async Task<HashSet<int>> BuildUsageLookupAsync(IReadOnlyCollection<int> subscriptionPlanIds)
    {
        if (subscriptionPlanIds.Count == 0)
        {
            return [];
        }

        var userSubscriptions = await _context.UserSubscriptions
            .Where(x => subscriptionPlanIds.Contains(x.SubscriptionPlanId))
            .Select(x => x.SubscriptionPlanId)
            .ToListAsync();

        var userHistory = await _context.UserSubscriptionHistory
            .Where(x => subscriptionPlanIds.Contains(x.SubscriptionPlanId))
            .Select(x => x.SubscriptionPlanId)
            .ToListAsync();

        var payments = await _context.SubscriptionPayments
            .Where(x => subscriptionPlanIds.Contains(x.SubscriptionPlanId))
            .Select(x => x.SubscriptionPlanId)
            .ToListAsync();

        var organizationSubscriptions = await _context.OrganizationSubscriptions
            .Where(x => subscriptionPlanIds.Contains(x.SubscriptionPlanId))
            .Select(x => x.SubscriptionPlanId)
            .ToListAsync();

        return userSubscriptions
            .Concat(userHistory)
            .Concat(payments)
            .Concat(organizationSubscriptions)
            .ToHashSet();
    }
}
