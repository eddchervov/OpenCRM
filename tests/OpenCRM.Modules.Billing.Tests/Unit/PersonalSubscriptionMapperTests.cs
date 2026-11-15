using Helpers;
using OpenCRM.Domain;

namespace OpenCRM.Modules.Billing.Tests.Unit;

public sealed class PersonalSubscriptionMapperTests
{
    public static TheoryData<string?, string?, string> FeatureNames => new()
    {
        { " EXPORT-PDF ", "Старое", "Экспорт PDF" },
        { "export-pdf-with-media", "Старое", "Экспорт PDF с фото" },
        { "export-word", "Старое", "Экспорт Word" },
        { "export-word-with-media", "Старое", "Экспорт Word с фото" },
        { "vehicle-limit", "Старое", "Лимит техники" },
        { FeatureCodes.VehiclesLimit, "Старое", "Лимит техники" },
        { FeatureCodes.WorkspaceVehiclesLimit, "Старое", "Лимит техники workspace" },
        { FeatureCodes.WorkspaceMembersLimit, "Старое", "Лимит участников" },
        { FeatureCodes.ClientsModule, "Старое", "Клиенты" },
        { FeatureCodes.DefectsModule, "Старое", "Неисправности" },
        { FeatureCodes.CountersModule, "Старое", "Пробег и наработка" },
        { FeatureCodes.MaintenanceModule, "Старое", "Обслуживание" },
        { FeatureCodes.SchedulerModule, "Старое", "Планировщик" },
        { FeatureCodes.RemindersModule, "Старое", "Напоминания" },
        { FeatureCodes.DocumentsModule, "Старое", "Документы" },
        { FeatureCodes.MaterialsModule, "Старое", "Материалы и запчасти" },
        { FeatureCodes.WorkJournalModule, "Старое", "Журнал работ" },
        { FeatureCodes.FinanceModule, "Старое", "Финансы" },
        { FeatureCodes.WikiModule, "Старое", "Вики организации" },
        { FeatureCodes.MembersModule, "Старое", "Участники" },
        { FeatureCodes.BillingModule, "Старое", "Тарифы и оплата" },
        { FeatureCodes.SettingsModule, "Старое", "Настройки организации" },
        { "custom", "  Пользовательская  ", "Пользовательская" },
        { "custom", " ", "Функция" },
        { null, null, "Функция" }
    };

    [Theory]
    [MemberData(nameof(FeatureNames))]
    public void CurrentPlanAndHistoryKeepDisplayNamesAndDifferentMetadata(string? code, string? name, string expected)
    {
        var plan = new SubscriptionPlan { Id = 4, Name = "Тариф", Description = "Описание", Price = 100,
            DiscountPrice = 75, VehicleLimit = 12, DurationDays = 31, Order = 3, Group = "Группа", PlanTypeCode = "unknown",
            Features = [new SubscriptionPlanFeature { FeatureDefinitionId = 7,
                FeatureDefinition = new FeatureDefinition { Id = 7, Code = code!, Name = name!, Category = "Категория",
                    ScopeCode = FeatureCatalogCodes.Scopes.Both, AudienceCode = FeatureCatalogCodes.Audiences.User,
                    ValueType = FeatureValueType.Integer }, IntValue = 8 }] };
        var current = PersonalSubscriptionMapper.MapPlan(plan);
        var history = PersonalSubscriptionMapper.MapHistory(new UserSubscriptionHistory { SubscriptionPlan = plan });

        Assert.Equal(expected, Assert.Single(current.Features).Name);
        Assert.Equal(expected, Assert.Single(history.SubscriptionPlan.Features).Name);
        Assert.Equal("Категория", current.Features[0].Category);
        Assert.Null(history.SubscriptionPlan.Features[0].Category);
        Assert.Equal(string.Empty, history.SubscriptionPlan.Features[0].AudienceCode);
        Assert.Equal(FeatureCatalogCodes.Scopes.Both, current.Features[0].ScopeCode);
        Assert.Equal(75, current.EffectivePrice);
        Assert.Equal(75, current.OriginalEffectivePrice);
        Assert.Equal(75, history.Amount);
        Assert.Equal(SubscriptionPlanCatalogCodes.NormalizePlanType(plan.PlanTypeCode), current.PlanTypeCode);
        Assert.Equal(current.PlanTypeCode, history.SubscriptionPlan.PlanTypeCode);
    }
}
