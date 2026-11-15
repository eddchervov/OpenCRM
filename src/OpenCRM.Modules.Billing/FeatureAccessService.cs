using Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Billing.Features;
using OpenCRM.Services.Interfaces.Features;
using OpenCRM.Services.Interfaces.Features.Dtos;

namespace OpenCRM.Modules.Billing;

/// <summary> Сервис проверки доступа к возможностям. </summary>
internal sealed class FeatureAccessService(
    OpenCRMContext context,
    IFeatureDefaultsInitializer featureDefaultsInitializer,
    IPlatformCommercialPolicy? platformCommercialPolicy = null,
    IConfiguration? configuration = null) : IFeatureAccessService
{
    /// <summary> Проверяет наличие возможности у пользователя. </summary>
    public async Task<bool> HasFeatureAsync(string userId, string featureCode)
    {
        var feature = await this.ResolveFeatureAsync(userId, featureCode);
        return feature.BoolValue == true;
    }

    /// <summary> Возвращает целочисленное значение возможности. </summary>
    public async Task<int> GetIntValueAsync(string userId, string featureCode, int defaultValue = 0)
    {
        var feature = await this.ResolveFeatureAsync(userId, featureCode);
        return feature.IntValue ?? defaultValue;
    }

    /// <summary> Возвращает снимок доступных возможностей пользователя. </summary>
    public async Task<FeatureAccessSnapshotDto> GetSnapshotAsync(string userId)
    {
        await featureDefaultsInitializer.EnsureDefaultsAsync();

        var definitions = await context.FeatureDefinitions
            .Where(x => x.IsActive && (x.ScopeCode == FeatureCatalogCodes.Scopes.Personal || x.ScopeCode == FeatureCatalogCodes.Scopes.Both))
            .OrderBy(x => x.Category)
            .ThenBy(x => x.Name)
            .ToListAsync();

        var snapshot = new FeatureAccessSnapshotDto();
        foreach (var definition in definitions)
        {
            if (platformCommercialPolicy?.CommercialEnabled != true && !FeatureCodes.All.Contains(definition.Code))
            {
                continue;
            }

            var resolved = await this.ResolveFeatureAsync(userId, definition.Code);
            snapshot.Features.Add(resolved);
        }

        return snapshot;
    }

    /// <summary> Определяет значение возможности для пользователя. </summary>
    private async Task<FeatureValueDto> ResolveFeatureAsync(string userId, string featureCode)
    {
        await featureDefaultsInitializer.EnsureDefaultsAsync();

        var definition = await context.FeatureDefinitions
            .FirstOrDefaultAsync(x => x.Code == featureCode && x.IsActive)
            ?? throw new InvalidOperationException($"Не найдена активная фича с кодом '{featureCode}'.");

        if (platformCommercialPolicy?.CommercialEnabled != true)
        {
            return FeatureAccessService.CreatePlatformValue(definition);
        }

        var now = DateTime.UtcNow;

        var userGrants = await context.UserFeatureGrants
            .Where(x => x.UserId == userId
                        && x.FeatureDefinitionId == definition.Id
                        && x.IsEnabled
                        && (!x.StartDate.HasValue || x.StartDate <= now)
                        && (!x.EndDate.HasValue || x.EndDate >= now))
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();

        if (definition.ValueType == FeatureValueType.Boolean)
        {
            var userBool = userGrants
                .Select(x => x.BoolValue)
                .FirstOrDefault(x => x.HasValue);

            if (userBool.HasValue)
            {
                return FeatureAccessService.CreateBoolValue(definition, userBool.Value);
            }
        }
        else if (definition.ValueType == FeatureValueType.Integer)
        {
            var userInt = userGrants
                .Where(x => x.IntValue.HasValue)
                .Select(x => x.IntValue!.Value)
                .DefaultIfEmpty(int.MinValue)
                .Max();

            if (userInt != int.MinValue)
            {
                return FeatureAccessService.CreateIntValue(definition, userInt);
            }
        }

        var activeSubscription = await context.UserSubscriptions
            .AsNoTracking()
            .Include(x => x.SubscriptionPlan)
            .FirstOrDefaultAsync(x => x.UserId == userId && x.EndDate >= now);

        if (activeSubscription != null)
        {
            var effectivePurchase = await SubscriptionPurchaseSnapshotResolver.GetForUserAsync(context, userId,
                activeSubscription.SubscriptionPlanId, activeSubscription.SubscriptionPurchaseId, activeSubscription.SourceCode, now);
            if (effectivePurchase != null)
            {
                activeSubscription.SubscriptionPlan = SubscriptionPlanSnapshot.Restore(effectivePurchase.PlanSnapshot);
            }

            var planFeature = effectivePurchase != null
                ? activeSubscription.SubscriptionPlan.Features.FirstOrDefault(item => item.FeatureDefinitionId == definition.Id)
                : await context.SubscriptionPlanFeatures
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.SubscriptionPlanId == activeSubscription.SubscriptionPlanId &&
                    x.FeatureDefinitionId == definition.Id);

            if (planFeature != null)
            {
                if (definition.ValueType == FeatureValueType.Boolean && planFeature.BoolValue.HasValue)
                {
                    return FeatureAccessService.CreateBoolValue(definition, planFeature.BoolValue.Value);
                }

                if (definition.ValueType == FeatureValueType.Integer && planFeature.IntValue.HasValue)
                {
                    return FeatureAccessService.CreateIntValue(definition, planFeature.IntValue.Value);
                }
            }

            return definition.Code == FeatureCodes.VehiclesLimit
                ? FeatureAccessService.CreateIntValue(definition, activeSubscription.SubscriptionPlan.VehicleLimit)
                : FeatureAccessService.CreateBoolValue(definition, false);
        }

        var defaultPlan = await context.SubscriptionPlans
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.IsDefaultPlan &&
                x.IsActive &&
                x.PlanTypeCode == SubscriptionPlanCatalogCodes.PlanTypes.Personal);

        if (defaultPlan != null)
        {
            var defaultPlanFeature = await context.SubscriptionPlanFeatures
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.SubscriptionPlanId == defaultPlan.Id &&
                    x.FeatureDefinitionId == definition.Id);

            if (defaultPlanFeature != null)
            {
                if (definition.ValueType == FeatureValueType.Boolean && defaultPlanFeature.BoolValue.HasValue)
                {
                    return FeatureAccessService.CreateBoolValue(definition, defaultPlanFeature.BoolValue.Value);
                }

                if (definition.ValueType == FeatureValueType.Integer && defaultPlanFeature.IntValue.HasValue)
                {
                    return FeatureAccessService.CreateIntValue(definition, defaultPlanFeature.IntValue.Value);
                }
            }

            if (definition.Code == FeatureCodes.VehiclesLimit)
            {
                return FeatureAccessService.CreateIntValue(definition, defaultPlan.VehicleLimit);
            }
        }

        var personalVehicleLimit = platformCommercialPolicy?.CommercialEnabled == true && configuration is not null
            ? CommercialPlanDefaults.FromConfiguration(configuration).PersonalVehicleLimit
            : PlatformDefaults.VehicleLimit;

        return FeatureAccessService.CreateDefaultValue(definition, personalVehicleLimit);
    }

    /// <summary> Возвращает возможности ядра независимо от сохранённых тарифов и грантов. </summary>
    internal static FeatureValueDto CreatePlatformValue(FeatureDefinition definition)
    {
        if (!FeatureCodes.All.Contains(definition.Code))
        {
            throw new InvalidOperationException($"Неизвестная возможность '{definition.Code}'.");
        }

        var value = definition.ValueType == FeatureValueType.Integer
            ? FeatureAccessService.CreateIntValue(definition, 0)
            : FeatureAccessService.CreateBoolValue(definition, definition.Code != FeatureCodes.BillingModule);
        value.Category = "Возможности";

        return value;
    }

    /// <summary> Формирует значение по умолчанию для возможности. </summary>
    private static FeatureValueDto CreateDefaultValue(FeatureDefinition definition, int personalVehicleLimit)
    {
        if (definition.Code == FeatureCodes.VehiclesLimit)
        {
            return FeatureAccessService.CreateIntValue(definition, personalVehicleLimit);
        }

        return FeatureAccessService.CreateBoolValue(definition, false);
    }

    /// <summary> Формирует булево значение возможности. </summary>
    private static FeatureValueDto CreateBoolValue(FeatureDefinition definition, bool value)
    {
        return new FeatureValueDto
        {
            FeatureId = definition.Id,
            Code = definition.Code,
            Name = definition.Name,
            Category = definition.Category,
            ScopeCode = definition.ScopeCode,
            ScopeName = FeatureCatalogCodes.Scopes.GetName(definition.ScopeCode),
            AudienceCode = definition.AudienceCode,
            AudienceName = FeatureCatalogCodes.Audiences.GetName(definition.AudienceCode),
            ValueType = definition.ValueType.ToString(),
            BoolValue = value
        };
    }

    /// <summary> Формирует целочисленное значение возможности. </summary>
    private static FeatureValueDto CreateIntValue(FeatureDefinition definition, int value)
    {
        return new FeatureValueDto
        {
            FeatureId = definition.Id,
            Code = definition.Code,
            Name = definition.Name,
            Category = definition.Category,
            ScopeCode = definition.ScopeCode,
            ScopeName = FeatureCatalogCodes.Scopes.GetName(definition.ScopeCode),
            AudienceCode = definition.AudienceCode,
            AudienceName = FeatureCatalogCodes.Audiences.GetName(definition.AudienceCode),
            ValueType = definition.ValueType.ToString(),
            IntValue = value
        };
    }
}
