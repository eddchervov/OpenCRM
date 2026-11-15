using Helpers;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Billing.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Features.Dtos;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using OpenCRM.Services.Interfaces.Subscriptions.Dtos;

namespace OpenCRM.Modules.Billing;

/// <summary> Читатель данных подписок и возможностей организации. </summary>
internal sealed class OrganizationEntitlementReader(
    OpenCRMContext context,
    IPlatformCommercialPolicy platformCommercialPolicy)
{
    /// <summary> Код источника промо-доступа. </summary>
    private const string PromoSourceCode = "promo";

    /// <summary> Проверяет право пользователя на чтение данных организации. </summary>
    public async Task EnsureCanReadAsync(string userId, Guid organizationId)
    {
        var canRead = await context.Organizations
            .AsNoTracking()
            .AnyAsync(organization => organization.Id == organizationId
                && !organization.IsDeleted
                && !organization.IsArchived
                && organization.IsActive
                && organization.Members.Any(member => member.UserId == userId));

        if (!canRead)
        {
            throw new InvalidOperationException("Организация не найдена или доступ закрыт.");
        }
    }

    /// <summary> Возвращает текущую подписку организации. </summary>
    public async Task<OrganizationSubscriptionDto?> GetCurrentAsync(Guid organizationId)
    {
        if (!platformCommercialPolicy.CommercialEnabled)
        {
            return null;
        }

        var subscription = await this.LoadCurrentSubscriptionAsync(organizationId);
        return subscription is null ? null : SubscriptionEntitlementMapper.MapSubscription(subscription);
    }

    /// <summary> Возвращает действующий тариф организации. </summary>
    public async Task<SubscriptionPlanDto?> GetEffectivePlanAsync(Guid organizationId)
    {
        if (!platformCommercialPolicy.CommercialEnabled)
        {
            return null;
        }

        var (_, effectivePlan) = await this.ResolveEffectivePlanAsync(organizationId);
        return effectivePlan is null ? null : SubscriptionEntitlementMapper.MapPlan(effectivePlan);
    }

    /// <summary> Проверяет наличие активной подписки организации. </summary>
    public Task<bool> HasActiveSubscriptionAsync(Guid organizationId)
    {
        if (!platformCommercialPolicy.CommercialEnabled)
        {
            return Task.FromResult(false);
        }

        return context.OrganizationSubscriptions
            .AsNoTracking()
            .AnyAsync(subscription => subscription.OrganizationId == organizationId
                && subscription.EndDate >= DateTime.UtcNow);
    }

    /// <summary> Возвращает снимок возможностей организации. </summary>
    public async Task<FeatureAccessSnapshotDto> GetFeatureSnapshotAsync(Guid organizationId)
    {
        var definitions = await context.FeatureDefinitions
            .AsNoTracking()
            .Where(definition => definition.IsActive
                && (definition.ScopeCode == FeatureCatalogCodes.Scopes.Organization
                    || definition.ScopeCode == FeatureCatalogCodes.Scopes.Both))
            .OrderBy(definition => definition.Category)
            .ThenBy(definition => definition.Name)
            .ToListAsync();

        if (!platformCommercialPolicy.CommercialEnabled)
        {
            return new FeatureAccessSnapshotDto
            {
                Features = definitions.Where(definition => FeatureCodes.All.Contains(definition.Code))
                    .Select(FeatureAccessService.CreatePlatformValue).ToList()
            };
        }

        var (currentSubscription, effectivePlan) = await this.ResolveEffectivePlanAsync(organizationId);
        var featuresByCode = effectivePlan?.Features
            .ToDictionary(feature => feature.FeatureDefinition.Code, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, SubscriptionPlanFeature>(StringComparer.OrdinalIgnoreCase);

        var snapshot = new FeatureAccessSnapshotDto();
        foreach (var definition in definitions)
        {
            featuresByCode.TryGetValue(definition.Code, out var assignment);
            if (assignment is null && currentSubscription is not null
                && definition.Code is FeatureCodes.MaintenanceIntake or FeatureCodes.Assistant or FeatureCodes.ClientMessaging
                    or FeatureCodes.DocumentStyling or FeatureCodes.DataImport or FeatureCodes.FinanceExport1C)
            {
                // Старые снимки покупок не содержат новых кодов; ранее доступные действия сохраняются до конца подписки.
                assignment = new SubscriptionPlanFeature { BoolValue = true };
            }

            snapshot.Features.Add(SubscriptionEntitlementMapper.MapSnapshotFeatureValue(
                definition,
                assignment,
                OrganizationEntitlementReader.GetDefaultIntegerValue(definition, effectivePlan)));
        }

        return snapshot;
    }

    /// <summary> Проверяет доступность демо-подписки организации. </summary>
    public async Task<(bool Available, string Message)> CheckDemoAvailabilityAsync(Guid organizationId)
    {
        if (!platformCommercialPolicy.CommercialEnabled)
        {
            return (false, "Коммерческий режим выключен.");
        }

        var organization = await context.Organizations
            .AsNoTracking()
            .Where(item => item.Id == organizationId && !item.IsDeleted && !item.IsArchived && item.IsActive)
            .Select(item => new { item.Id, item.OwnerUserId, item.PrimaryScenarioCode })
            .FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("Организация не найдена.");

        if (organization.PrimaryScenarioCode is OrganizationTypeCodes.Detailing or OrganizationTypeCodes.ServiceStation)
        {
            return (false, "Пробный доступ не настроен: рабочее пространство работает без коммерческой подписки.");
        }

        var currentSubscription = await context.OrganizationSubscriptions
            .AsNoTracking()
            .Include(subscription => subscription.SubscriptionPlan)
            .Where(subscription => subscription.OrganizationId == organizationId
                && subscription.EndDate >= DateTime.UtcNow)
            .OrderByDescending(subscription => subscription.EndDate)
            .FirstOrDefaultAsync();

        if (currentSubscription?.SubscriptionPlan.IsDemoPlan == true)
        {
            return (false, "Демо-тариф уже активен.");
        }

        if (currentSubscription is not null)
        {
            return (false, "У рабочего пространства уже есть активный бизнес-тариф.");
        }

        // Подарочная история не расходует демо-доступность: учитываем только реальные демо/платные записи.
        var hasSubscriptionHistory = await context.OrganizationSubscriptions
            .AsNoTracking()
            .AnyAsync(subscription => subscription.OrganizationId == organizationId
                && subscription.SourceCode != PromoSourceCode);
        if (hasSubscriptionHistory)
        {
            return (false, "Демо-тариф доступен только рабочим пространствам без истории демо или платных тарифов.");
        }

        var firstScenarioOrganizationId = await context.Organizations
            .AsNoTracking()
            .Where(item => item.OwnerUserId == organization.OwnerUserId
                && item.PrimaryScenarioCode == organization.PrimaryScenarioCode)
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .Select(item => item.Id)
            .FirstOrDefaultAsync();

        if (firstScenarioOrganizationId != organizationId)
        {
            return (false, "Демо-тариф доступен только для первого рабочего пространства такого типа.");
        }

        return (true, "Демо-тариф доступен.");
    }

    /// <summary> Возвращает права рабочего пространства организации. </summary>
    public async Task<OrganizationWorkspaceEntitlementDto> GetWorkspaceEntitlementAsync(Guid organizationId)
    {
        var organization = await context.Organizations
            .AsNoTracking()
            .Where(item => item.Id == organizationId && !item.IsDeleted && !item.IsArchived && item.IsActive)
            .Select(item => new { item.OwnerUserId })
            .FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("Организация не найдена.");

        var baseWorkspaceId = await context.Organizations
            .AsNoTracking()
            .Where(item => item.OwnerUserId == organization.OwnerUserId
                && !item.IsDeleted
                && !item.IsArchived
                && item.IsActive)
            .OrderBy(item => item.CreatedAt)
            .Select(item => item.Id)
            .FirstOrDefaultAsync();

        if (!platformCommercialPolicy.CommercialEnabled)
        {
            return new OrganizationWorkspaceEntitlementDto
            {
                IsFreePlan = true,
                IsBaseWorkspace = baseWorkspaceId == organizationId,
                HasActiveSubscription = false,
                HasSubscriptionHistory = false
            };
        }

        var (currentSubscription, effectivePlan) = await this.ResolveEffectivePlanAsync(organizationId);
        var hasSubscriptionHistory = await context.OrganizationSubscriptions
            .AsNoTracking()
            .AnyAsync(subscription => subscription.OrganizationId == organizationId);

        return new OrganizationWorkspaceEntitlementDto
        {
            IsFreePlan = effectivePlan is null
                // Базовая поставка работает без тарифов: права рабочего пространства —
                // как у бесплатного тарифа, иначе нейтральная установка не смогла бы читать и писать.
                ? !platformCommercialPolicy.CommercialEnabled
                : effectivePlan.Group == SubscriptionPlanCatalogCodes.OrganizationFreeGroup,
            IsBaseWorkspace = baseWorkspaceId == organizationId,
            HasActiveSubscription = currentSubscription is not null,
            HasSubscriptionHistory = hasSubscriptionHistory
        };
    }

    /// <summary> Загружает текущую подписку организации. </summary>
    private async Task<OrganizationSubscription?> LoadCurrentSubscriptionAsync(Guid organizationId)
    {
        var now = DateTime.UtcNow;
        var subscription = await context.OrganizationSubscriptions
            .AsNoTracking()
            .Include(subscription => subscription.SubscriptionPlan)
                .ThenInclude(plan => plan.Features)
                    .ThenInclude(feature => feature.FeatureDefinition)
            .Where(subscription => subscription.OrganizationId == organizationId
                && subscription.EndDate >= DateTime.UtcNow)
            .OrderByDescending(subscription => subscription.EndDate)
            .FirstOrDefaultAsync();
        if (subscription != null)
        {
            var effectivePurchase = await SubscriptionPurchaseSnapshotResolver.GetForOrganizationAsync(context, organizationId,
                subscription.SubscriptionPlanId, subscription.SubscriptionPurchaseId, subscription.SourceCode, now);
            if (effectivePurchase != null)
            {
                subscription.SubscriptionPlan = SubscriptionPlanSnapshot.Restore(effectivePurchase.PlanSnapshot);
            }
        }

        return subscription;
    }

    /// <summary> Определяет текущую подписку и действующий тариф организации. </summary>
    private async Task<(OrganizationSubscription? CurrentSubscription, SubscriptionPlan? EffectivePlan)> ResolveEffectivePlanAsync(
        Guid organizationId)
    {
        var currentSubscription = await this.LoadCurrentSubscriptionAsync(organizationId);
        if (currentSubscription is not null)
        {
            return (currentSubscription, currentSubscription.SubscriptionPlan);
        }

        var organization = await context.Organizations
            .AsNoTracking()
            .Where(item => item.Id == organizationId && !item.IsDeleted && !item.IsArchived && item.IsActive)
            .Select(item => new { item.PrimaryScenarioCode })
            .SingleOrDefaultAsync();

        if (organization is null)
        {
            throw new InvalidOperationException("Организация не найдена.");
        }

        if (organization.PrimaryScenarioCode is OrganizationTypeCodes.Detailing or OrganizationTypeCodes.ServiceStation)
        {
            // Без коммерческого режима бессрочного тарифа нет: рабочее пространство
            // работает на правах и участии, а не на фиктивной подписке.
            var freePlan = await context.SubscriptionPlans
                .AsNoTracking()
                .Include(plan => plan.Features)
                    .ThenInclude(feature => feature.FeatureDefinition)
                .FirstOrDefaultAsync(plan => plan.IsActive
                    && plan.PlanTypeCode == SubscriptionPlanCatalogCodes.PlanTypes.Business
                    && plan.Group == SubscriptionPlanCatalogCodes.OrganizationFreeGroup);

            return (null, freePlan);
        }

        var defaultBusinessPlan = await context.SubscriptionPlans
            .AsNoTracking()
            .Include(plan => plan.Features)
                .ThenInclude(feature => feature.FeatureDefinition)
            .FirstOrDefaultAsync(plan => plan.IsActive
                && plan.IsDefaultPlan
                && plan.PlanTypeCode == SubscriptionPlanCatalogCodes.PlanTypes.Business);

        return (null, defaultBusinessPlan);
    }

    /// <summary> Возвращает целочисленное значение возможности по умолчанию. </summary>
    private static int GetDefaultIntegerValue(FeatureDefinition definition, SubscriptionPlan? currentPlan)
    {
        if (definition.Code == FeatureCodes.WorkspaceClientsLimit)
        {
            return currentPlan?.Group == SubscriptionPlanCatalogCodes.OrganizationFreeGroup
                ? PlatformDefaults.ClientLimit
                : PlatformDefaults.ClientLimit;
        }

        if (definition.Code == FeatureCodes.WorkspaceVehiclesLimit)
        {
            return currentPlan?.VehicleLimit ?? PlatformDefaults.VehicleLimit;
        }

        return definition.Code == FeatureCodes.WorkspaceMembersLimit ? PlatformDefaults.WorkspaceMembersLimit : 0;
    }
}
