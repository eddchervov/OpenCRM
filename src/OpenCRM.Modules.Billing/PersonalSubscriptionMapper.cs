using Helpers;
using OpenCRM.Domain;
using OpenCRM.Services.Interfaces.Features.Dtos;
using OpenCRM.Services.Interfaces.Subscriptions.Dtos;

namespace OpenCRM.Modules.Billing;

/// <summary> Маппер сущностей личных подписок в DTO. </summary>
internal static class PersonalSubscriptionMapper
{
    /// <summary> Преобразует тариф в DTO. </summary>
    public static SubscriptionPlanDto MapPlan(SubscriptionPlan plan)
    {
        return PersonalSubscriptionMapper.MapPlan(plan, includeAccessMetadata: true);
    }

    /// <summary> Преобразует тариф в DTO с флагом метаданных доступа. </summary>
    private static SubscriptionPlanDto MapPlan(SubscriptionPlan plan, bool includeAccessMetadata)
    {
        return new SubscriptionPlanDto
        {
            VehicleLimit = plan.VehicleLimit,
            Description = plan.Description,
            DurationDays = plan.DurationDays,
            Id = plan.Id,
            IsActive = plan.IsActive,
            Name = plan.Name,
            Order = plan.Order,
            Price = plan.Price,
            DiscountPrice = plan.DiscountPrice,
            EffectivePrice = plan.EffectivePrice,
            OriginalEffectivePrice = plan.EffectivePrice,
            HasDiscount = plan.HasDiscount,
            Group = plan.Group,
            PlanTypeCode = SubscriptionPlanCatalogCodes.NormalizePlanType(plan.PlanTypeCode),
            PlanTypeName = SubscriptionPlanCatalogCodes.GetPlanTypeName(plan.PlanTypeCode),
            IsDemoPlan = plan.IsDemoPlan,
            IsDefaultPlan = plan.IsDefaultPlan,
            Features = plan.Features
                .OrderBy(feature => PersonalSubscriptionMapper.GetFeatureDisplayName(feature.FeatureDefinition.Code, feature.FeatureDefinition.Name))
                .Select(feature => PersonalSubscriptionMapper.MapFeature(feature, includeAccessMetadata))
                .ToList()
        };
    }

    /// <summary> Преобразует подписку пользователя в DTO. </summary>
    public static UserSubscriptionDto? MapUserSubscription(UserSubscription? subscription)
    {
        if (subscription == null)
        {
            return null;
        }

        return new UserSubscriptionDto
        {
            AutoRenewal = subscription.AutoRenewal,
            EndDate = subscription.EndDate,
            Id = subscription.Id,
            StartDate = subscription.StartDate,
            SubscriptionPlan = PersonalSubscriptionMapper.MapPlan(subscription.SubscriptionPlan),
            SubscriptionPlanId = subscription.SubscriptionPlanId
        };
    }

    /// <summary> Преобразует запись истории подписки в DTO. </summary>
    public static SubscriptionHistoryDto MapHistory(UserSubscriptionHistory history)
    {
        return new SubscriptionHistoryDto
        {
            Id = history.Id,
            SubscriptionPlan = PersonalSubscriptionMapper.MapPlan(history.SubscriptionPlan, includeAccessMetadata: false),
            StartDate = history.StartDate,
            EndDate = history.EndDate,
            ActualEndDate = history.ActualEndDate,
            CreatedAt = history.CreatedAt,
            Comment = history.Comment,
            Amount = history.SubscriptionPlan.EffectivePrice,
            Status = history.Status
        };
    }

    /// <summary> Преобразует функцию тарифа в DTO. </summary>
    private static FeatureValueDto MapFeature(SubscriptionPlanFeature feature, bool includeAccessMetadata)
    {
        var definition = feature.FeatureDefinition;
        var displayName = PersonalSubscriptionMapper.GetFeatureDisplayName(definition.Code, definition.Name);

        var result = new FeatureValueDto
        {
            FeatureId = feature.FeatureDefinitionId,
            Code = definition.Code,
            Name = displayName,
            ValueType = definition.ValueType.ToString(),
            BoolValue = feature.BoolValue,
            IntValue = feature.IntValue
        };

        if (!includeAccessMetadata)
        {
            return result;
        }

        result.Category = definition.Category;
        result.ScopeCode = definition.ScopeCode;
        result.ScopeName = FeatureCatalogCodes.Scopes.GetName(definition.ScopeCode);
        result.AudienceCode = definition.AudienceCode;
        result.AudienceName = FeatureCatalogCodes.Audiences.GetName(definition.AudienceCode);

        return result;
    }

    /// <summary> Определяет отображаемое имя функции по коду и названию. </summary>
    private static string GetFeatureDisplayName(string? code, string? name)
    {
        return code?.Trim().ToLowerInvariant() switch
        {
            "export-pdf" => "Экспорт PDF",
            "export-pdf-with-media" => "Экспорт PDF с фото",
            "export-word" => "Экспорт Word",
            "export-word-with-media" => "Экспорт Word с фото",
            "vehicle-limit" => "Лимит техники",
            FeatureCodes.VehiclesLimit => "Лимит техники",
            FeatureCodes.WorkspaceVehiclesLimit => "Лимит техники workspace",
            FeatureCodes.WorkspaceMembersLimit => "Лимит участников",
            FeatureCodes.ClientsModule => "Клиенты",
            FeatureCodes.DefectsModule => "Неисправности",
            FeatureCodes.CountersModule => "Пробег и наработка",
            FeatureCodes.MaintenanceModule => "Обслуживание",
            FeatureCodes.SchedulerModule => "Планировщик",
            FeatureCodes.RemindersModule => "Напоминания",
            FeatureCodes.DocumentsModule => "Документы",
            FeatureCodes.MaterialsModule => "Материалы и запчасти",
            FeatureCodes.WorkJournalModule => "Журнал работ",
            FeatureCodes.FinanceModule => "Финансы",
            FeatureCodes.WikiModule => "Вики организации",
            FeatureCodes.MembersModule => "Участники",
            FeatureCodes.BillingModule => "Тарифы и оплата",
            FeatureCodes.SettingsModule => "Настройки организации",
            _ => string.IsNullOrWhiteSpace(name) ? "Функция" : name.Trim()
        };
    }
}
