using Helpers;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;

namespace OpenCRM.Modules.Billing.Features;

/// <summary> Инициализатор значений возможностей по умолчанию. </summary>
public static class FeatureBootstrapper
{
    /// <summary> Блокировка инициализации. </summary>
    private static readonly SemaphoreSlim Locker = new(1, 1);

    /// <summary> Лимит участников рабочего пространства по умолчанию. </summary>
    private const int DefaultWorkspaceMembersLimit = PlatformDefaults.WorkspaceMembersLimit;

    /// <summary> Гарантирует наличие значений возможностей по умолчанию. </summary>
    public static async Task EnsureDefaultsAsync(OpenCRMContext context)
    {
        await Locker.WaitAsync();
        try
        {
            await EnsureDefaultDefinitionsAsync(context);
        }
        finally
        {
            Locker.Release();
        }
    }

    /// <summary>
    /// Гарантирует наличие тарифов коммерческого режима.
    /// Вызывается только когда коммерческий режим включён настройкой
    /// PlatformPolicy:Commercial:Enabled.
    /// </summary>
    public static async Task EnsureCommercialPlansAsync(OpenCRMContext context)
    {
        await Locker.WaitAsync();
        try
        {
            await EnsureDeprecatedDefinitionsAsync(context);
            await EnsureSystemPlansAsync(context);
            await EnsureDefaultPlanFeaturesAsync(context);
            await EnsureFreePlanAsync(context);
        }
        finally
        {
            Locker.Release();
        }
    }

    /// <summary> Гарантирует наличие определений возможностей по умолчанию. </summary>
    private static async Task EnsureDefaultDefinitionsAsync(OpenCRMContext context)
    {
        var defaults = GetDefaultDefinitions();
        var existingDefinitions = await context.FeatureDefinitions.ToListAsync();

        foreach (var feature in defaults)
        {
            var existingDefinition = existingDefinitions.FirstOrDefault(x => x.Code == feature.Code);
            if (existingDefinition == null)
            {
                context.FeatureDefinitions.Add(feature);
                continue;
            }

            if (existingDefinition.IsSystem != feature.IsSystem)
            {
                existingDefinition.IsSystem = feature.IsSystem;
            }
        }

        if (context.ChangeTracker.HasChanges())
        {
            await context.SaveChangesAsync();
        }
    }

    /// <summary> Гарантирует наличие привязок возможностей тарифов по умолчанию. </summary>
    private static async Task EnsureDefaultPlanFeaturesAsync(OpenCRMContext context)
    {
        var featureDefinitions = await context.FeatureDefinitions.ToListAsync();
        var plans = await context.SubscriptionPlans.ToListAsync();
        var existingAssignments = await context.SubscriptionPlanFeatures.ToListAsync();

        var vehiclesLimitFeatureId = featureDefinitions.First(x => x.Code == FeatureCodes.VehiclesLimit).Id;
        var workspaceVehiclesLimitFeatureId = featureDefinitions.First(x => x.Code == FeatureCodes.WorkspaceVehiclesLimit).Id;
        var workspaceMembersLimitFeatureId = featureDefinitions.First(x => x.Code == FeatureCodes.WorkspaceMembersLimit).Id;
        var featureIdsByCode = featureDefinitions.ToDictionary(x => x.Code, x => x.Id);
        var vehiclePhotoAlbumsFeatureId = featureDefinitions.First(x => x.Code == FeatureCodes.VehiclePhotoAlbums).Id;
        var defaultWorkspaceModules = new[]
        {
            FeatureCodes.VehiclesModule,
            FeatureCodes.ClientsModule,
            FeatureCodes.DefectsModule,
            FeatureCodes.CountersModule,
            FeatureCodes.MaintenanceModule,
            FeatureCodes.SchedulerModule,
            FeatureCodes.RemindersModule,
            FeatureCodes.DocumentsModule,
            FeatureCodes.MaterialsModule,
            FeatureCodes.WorkJournalModule,
            FeatureCodes.FinanceModule,
            FeatureCodes.AnalyticsModule,
            FeatureCodes.WikiModule,
            FeatureCodes.MembersModule,
            FeatureCodes.BillingModule,
            FeatureCodes.SettingsModule,
            FeatureCodes.ExportPdf,
            FeatureCodes.ExportPdfWithPhotos,
            FeatureCodes.ExportWord,
            FeatureCodes.ExportWordWithPhotos
        };
        var demoWorkspaceModules = new[]
        {
            FeatureCodes.VehiclesModule,
            FeatureCodes.ClientsModule,
            FeatureCodes.DefectsModule,
            FeatureCodes.CountersModule,
            FeatureCodes.MaintenanceModule,
            FeatureCodes.SchedulerModule,
            FeatureCodes.RemindersModule,
            FeatureCodes.DocumentsModule,
            FeatureCodes.MaterialsModule,
            FeatureCodes.WorkJournalModule,
            FeatureCodes.FinanceModule,
            FeatureCodes.AnalyticsModule,
            FeatureCodes.WikiModule,
            FeatureCodes.MembersModule,
            FeatureCodes.BillingModule,
            FeatureCodes.SettingsModule,
            FeatureCodes.ExportPdf,
            FeatureCodes.ExportPdfWithPhotos,
            FeatureCodes.ExportWord,
            FeatureCodes.ExportWordWithPhotos
        };

        foreach (var plan in plans)
        {
            if (plan.Group == SubscriptionPlanCatalogCodes.OrganizationFreeGroup)
            {
                continue;
            }

            EnsurePlanFeature(existingAssignments, context, plan.Id, vehiclePhotoAlbumsFeatureId, boolValue: true);

            if (plan.PlanTypeCode == SubscriptionPlanCatalogCodes.PlanTypes.Business)
            {
                EnsurePlanFeature(existingAssignments, context, plan.Id, featureIdsByCode[FeatureCodes.WorkspaceClientsLimit], intValue: int.MaxValue);
                EnsurePlanFeature(existingAssignments, context, plan.Id, featureIdsByCode[FeatureCodes.MaintenanceIntake], boolValue: true);
                EnsurePlanFeature(existingAssignments, context, plan.Id, featureIdsByCode[FeatureCodes.Assistant], boolValue: true);
                EnsurePlanFeature(existingAssignments, context, plan.Id, featureIdsByCode[FeatureCodes.ClientMessaging], boolValue: true);
                EnsurePlanFeature(existingAssignments, context, plan.Id, featureIdsByCode[FeatureCodes.DocumentStyling], boolValue: true);
                EnsurePlanFeature(existingAssignments, context, plan.Id, featureIdsByCode[FeatureCodes.DataImport], boolValue: true);
                EnsurePlanFeature(existingAssignments, context, plan.Id, featureIdsByCode[FeatureCodes.FinanceExport1C], boolValue: true);
                EnsurePlanFeature(existingAssignments, context, plan.Id, workspaceVehiclesLimitFeatureId, intValue: plan.VehicleLimit);
                EnsurePlanFeature(existingAssignments, context, plan.Id, workspaceMembersLimitFeatureId, intValue: DefaultWorkspaceMembersLimit);
                if (featureIdsByCode.TryGetValue(FeatureCodes.AnalyticsModule, out var analyticsModuleFeatureId))
                {
                    EnsurePlanFeature(existingAssignments, context, plan.Id, analyticsModuleFeatureId, boolValue: true);
                }
                if (featureIdsByCode.TryGetValue(FeatureCodes.FinanceModule, out var financeModuleFeatureId))
                {
                    EnsurePlanFeature(existingAssignments, context, plan.Id, financeModuleFeatureId, boolValue: true);
                }

                var defaultModuleCodes = plan.IsDemoPlan
                    ? demoWorkspaceModules
                    : plan.IsDefaultPlan ? defaultWorkspaceModules : Array.Empty<string>();

                foreach (var featureCode in defaultModuleCodes)
                {
                    if (featureIdsByCode.TryGetValue(featureCode, out var featureId))
                    {
                        EnsurePlanFeature(existingAssignments, context, plan.Id, featureId, boolValue: true);
                    }
                }

                continue;
            }

            EnsurePlanFeature(existingAssignments, context, plan.Id, vehiclesLimitFeatureId, intValue: plan.VehicleLimit);
        }

        if (context.ChangeTracker.HasChanges())
        {
            await context.SaveChangesAsync();
        }
    }

    /// <summary> Создаёт самостоятельный Free, сохраняя прежний базовый тариф и оплаченные условия. </summary>
    private static async Task EnsureFreePlanAsync(OpenCRMContext context)
    {
        const string FreePlanDescription = "Базовая работа одного владельца без ограничения срока.";
        var plan = await context.SubscriptionPlans.SingleOrDefaultAsync(item =>
            item.PlanTypeCode == SubscriptionPlanCatalogCodes.PlanTypes.Business
            && item.Group == SubscriptionPlanCatalogCodes.OrganizationFreeGroup);

        if (plan is null)
        {
            plan = new SubscriptionPlan
            {
                Name = "Бесплатный",
                Description = FreePlanDescription,
                Group = SubscriptionPlanCatalogCodes.OrganizationFreeGroup,
                PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Business,
                Price = 0,
                DurationDays = 30,
                VehicleLimit = PlatformDefaults.VehicleLimit,
                IsActive = true
            };

            context.SubscriptionPlans.Add(plan);
            await context.SaveChangesAsync();
        }

        var definitions = await context.FeatureDefinitions
            .Where(item => item.IsActive && item.ScopeCode != FeatureCatalogCodes.Scopes.Personal)
            .ToListAsync();

        var assignments = await context.SubscriptionPlanFeatures
            .Where(item => item.SubscriptionPlanId == plan.Id)
            .ToListAsync();

        var enabledCodes = new HashSet<string>(StringComparer.Ordinal)
        {
            FeatureCodes.VehiclesModule,
            FeatureCodes.ClientsModule,
            FeatureCodes.MaintenanceModule,
            FeatureCodes.CountersModule,
            FeatureCodes.SchedulerModule,
            FeatureCodes.DocumentsModule,
            FeatureCodes.WorkJournalModule,
            FeatureCodes.MaterialsModule,
            FeatureCodes.WikiModule,
            FeatureCodes.FinanceModule,
            FeatureCodes.BillingModule,
            FeatureCodes.SettingsModule,
            FeatureCodes.ExportPdf,
            FeatureCodes.ExportPdfWithPhotos,
            FeatureCodes.ExportWord,
            FeatureCodes.ExportWordWithPhotos
        };

        foreach (var definition in definitions)
        {
            if (definition.ValueType == FeatureValueType.Boolean)
            {
                EnsurePlanFeature(assignments, context, plan.Id, definition.Id, boolValue: enabledCodes.Contains(definition.Code));

                continue;
            }

            var limit = definition.Code switch
            {
                FeatureCodes.WorkspaceVehiclesLimit => plan.VehicleLimit,
                FeatureCodes.WorkspaceClientsLimit => PlatformDefaults.ClientLimit,
                FeatureCodes.WorkspaceMembersLimit => PlatformDefaults.WorkspaceMembersLimit,
                _ => 0
            };

            EnsurePlanFeature(assignments, context, plan.Id, definition.Id, intValue: limit);
        }

        if (context.ChangeTracker.HasChanges())
        {
            await context.SaveChangesAsync();
        }
    }

    /// <summary> Гарантирует наличие устаревших определений возможностей. </summary>
    private static async Task EnsureDeprecatedDefinitionsAsync(OpenCRMContext context)
    {
        var deprecatedDefinitions = await context.FeatureDefinitions
            .Where(x => x.Code == "workspace.count.limit")
            .ToListAsync();

        if (deprecatedDefinitions.Count == 0)
        {
            return;
        }

        foreach (var deprecatedDefinition in deprecatedDefinitions)
        {
            if (deprecatedDefinition.IsActive)
            {
                deprecatedDefinition.IsActive = false;
            }
        }

        var deprecatedDefinitionIds = deprecatedDefinitions
            .Select(x => x.Id)
            .ToList();

        var deprecatedAssignments = await context.SubscriptionPlanFeatures
            .Where(x => deprecatedDefinitionIds.Contains(x.FeatureDefinitionId))
            .ToListAsync();

        if (deprecatedAssignments.Count > 0)
        {
            context.SubscriptionPlanFeatures.RemoveRange(deprecatedAssignments);
        }

        if (context.ChangeTracker.HasChanges())
        {
            await context.SaveChangesAsync();
        }
    }

    /// <summary> Гарантирует наличие системных тарифов. </summary>
    private static async Task EnsureSystemPlansAsync(OpenCRMContext context)
    {
        var businessDefaultPlan = await context.SubscriptionPlans
            .FirstOrDefaultAsync(x =>
                x.PlanTypeCode == SubscriptionPlanCatalogCodes.PlanTypes.Business &&
                x.IsDefaultPlan);

        if (businessDefaultPlan == null)
        {
            context.SubscriptionPlans.Add(new SubscriptionPlan
            {
                Name = "Базовое рабочее пространство",
                Description = "Базовый бизнес-тариф для первого рабочего пространства.",
                Price = 0,
                DurationDays = 30,
                VehicleLimit = PlatformDefaults.VehicleLimit,
                Order = 0,
                Group = "Базовый",
                PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Business,
                IsActive = true,
                IsDemoPlan = false,
                IsDefaultPlan = true
            });

            await context.SaveChangesAsync();
        }

        var businessDemoPlan = await context.SubscriptionPlans
            .FirstOrDefaultAsync(x =>
                x.PlanTypeCode == SubscriptionPlanCatalogCodes.PlanTypes.Business &&
                x.IsDemoPlan);

        if (businessDemoPlan == null)
        {
            context.SubscriptionPlans.Add(new SubscriptionPlan
            {
                Name = "Демо рабочего пространства",
                Description = "Тестовый бизнес-тариф для рабочего пространства организации.",
                Price = 0,
                DurationDays = 30,
                VehicleLimit = PlatformDefaults.VehicleLimit,
                Order = 0,
                Group = "Демо",
                PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Business,
                IsActive = true,
                IsDemoPlan = true,
                IsDefaultPlan = false
            });

            await context.SaveChangesAsync();
            return;
        }

        var hasChanges = false;

        if (businessDemoPlan.Name == "Демо workspace")
        {
            businessDemoPlan.Name = "Демо рабочего пространства";
            hasChanges = true;
        }

        if (!businessDemoPlan.IsDemoPlan)
        {
            businessDemoPlan.IsDemoPlan = true;
            hasChanges = true;
        }

        if (businessDemoPlan.PlanTypeCode != SubscriptionPlanCatalogCodes.PlanTypes.Business)
        {
            businessDemoPlan.PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Business;
            hasChanges = true;
        }

        if (businessDemoPlan.IsDefaultPlan)
        {
            businessDemoPlan.IsDefaultPlan = false;
            hasChanges = true;
        }

        if (hasChanges)
        {
            await context.SaveChangesAsync();
        }
    }

    /// <summary> Добавляет привязку возможности к тарифу при отсутствии. </summary>
    private static void EnsurePlanFeature(
        List<SubscriptionPlanFeature> existingAssignments,
        OpenCRMContext context,
        int planId,
        int featureId,
        bool? boolValue = null,
        int? intValue = null)
    {
        if (existingAssignments.Any(x => x.SubscriptionPlanId == planId && x.FeatureDefinitionId == featureId))
        {
            return;
        }

        var assignment = new SubscriptionPlanFeature
        {
            SubscriptionPlanId = planId,
            FeatureDefinitionId = featureId,
            BoolValue = boolValue,
            IntValue = intValue
        };

        existingAssignments.Add(assignment);
        context.SubscriptionPlanFeatures.Add(assignment);
    }

    /// <summary> Возвращает список определений возможностей по умолчанию. </summary>
    private static List<FeatureDefinition> GetDefaultDefinitions()
    {
        return
        [
            new FeatureDefinition
            {
                Code = FeatureCodes.WorkspaceClientsLimit,
                Name = "Лимит клиентов организации",
                Description = "Максимальное количество клиентов организации, включая архивные карточки.",
                Category = "Лимиты",
                ScopeCode = FeatureCatalogCodes.Scopes.Organization,
                AudienceCode = FeatureCatalogCodes.Audiences.User,
                ValueType = FeatureValueType.Integer,
                IsActive = true,
                IsSystem = true
            },
            new FeatureDefinition
            {
                Code = FeatureCodes.DocumentStyling,
                Name = "Оформление документов",
                Description = "Выбор стиля печатных PDF и Word организации.",
                Category = "PRO",
                ScopeCode = FeatureCatalogCodes.Scopes.Organization,
                AudienceCode = FeatureCatalogCodes.Audiences.User,
                ValueType = FeatureValueType.Boolean,
                IsActive = true,
                IsSystem = true
            },
            new FeatureDefinition
            {
                Code = FeatureCodes.DataImport,
                Name = "Импорт данных",
                Description = "Загрузка, проверка и применение импорта данных организации.",
                Category = "PRO",
                ScopeCode = FeatureCatalogCodes.Scopes.Organization,
                AudienceCode = FeatureCatalogCodes.Audiences.User,
                ValueType = FeatureValueType.Boolean,
                IsActive = true,
                IsSystem = true
            },
            new FeatureDefinition
            {
                Code = FeatureCodes.FinanceExport1C,
                Name = "Выгрузка для 1С:Бухгалтерии 3.0",
                Description = "Подготовка бухгалтерской выгрузки для 1С; универсальный JSON доступен отдельно.",
                Category = "PRO",
                ScopeCode = FeatureCatalogCodes.Scopes.Organization,
                AudienceCode = FeatureCatalogCodes.Audiences.User,
                ValueType = FeatureValueType.Boolean,
                IsActive = true,
                IsSystem = true
            },
            new FeatureDefinition
            {
                Code = FeatureCodes.MaintenanceIntake,
                Name = "Расширенная приёмка",
                Description = "Осмотр, повреждения, фотографии и акт приёма-передачи автомобиля.",
                Category = "PRO",
                ScopeCode = FeatureCatalogCodes.Scopes.Organization,
                AudienceCode = FeatureCatalogCodes.Audiences.User,
                ValueType = FeatureValueType.Boolean,
                IsActive = true,
                IsSystem = true
            },
            new FeatureDefinition
            {
                Code = FeatureCodes.Assistant,
                Name = "ИИ-помощник",
                Description = "Помощник на сайте и в служебном Telegram с отдельным бюджетом использования.",
                Category = "PRO",
                ScopeCode = FeatureCatalogCodes.Scopes.Organization,
                AudienceCode = FeatureCatalogCodes.Audiences.User,
                ValueType = FeatureValueType.Boolean,
                IsActive = true,
                IsSystem = true
            },
            new FeatureDefinition
            {
                Code = FeatureCodes.ClientMessaging,
                Name = "Боты Telegram и MAX",
                Description = "Подключение ботов организации, переписка с клиентами и автоматические сообщения.",
                Category = "PRO",
                ScopeCode = FeatureCatalogCodes.Scopes.Organization,
                AudienceCode = FeatureCatalogCodes.Audiences.User,
                ValueType = FeatureValueType.Boolean,
                IsActive = true,
                IsSystem = true
            },
            new FeatureDefinition
            {
                Code = FeatureCodes.VehiclesLimit,
                Name = "Лимит техники",
                Description = "Определяет максимальное количество единиц техники для аккаунта или подписки.",
                Category = "Лимиты",
                ScopeCode = FeatureCatalogCodes.Scopes.Personal,
                AudienceCode = FeatureCatalogCodes.Audiences.User,
                ValueType = FeatureValueType.Integer,
                IsActive = true,
                IsSystem = true
            },
            new FeatureDefinition
            {
                Code = FeatureCodes.WorkspaceVehiclesLimit,
                Name = "Лимит техники рабочего пространства",
                Description = "Определяет максимальное количество единиц техники в рабочем пространстве организации.",
                Category = "Лимиты",
                ScopeCode = FeatureCatalogCodes.Scopes.Organization,
                AudienceCode = FeatureCatalogCodes.Audiences.Admin,
                ValueType = FeatureValueType.Integer,
                IsActive = true,
                IsSystem = true
            },
            new FeatureDefinition
            {
                Code = FeatureCodes.WorkspaceMembersLimit,
                Name = "Лимит участников рабочего пространства",
                Description = "Определяет максимальное количество участников организации.",
                Category = "Лимиты",
                ScopeCode = FeatureCatalogCodes.Scopes.Organization,
                AudienceCode = FeatureCatalogCodes.Audiences.Admin,
                ValueType = FeatureValueType.Integer,
                IsActive = true,
                IsSystem = true
            },
            new FeatureDefinition
            {
                Code = FeatureCodes.VehiclesModule,
                Name = "Техника",
                Description = "Открывает отдельный раздел техники в рабочем пространстве организации.",
                Category = "Модули",
                ScopeCode = FeatureCatalogCodes.Scopes.Organization,
                AudienceCode = FeatureCatalogCodes.Audiences.User,
                ValueType = FeatureValueType.Boolean,
                IsActive = true,
                IsSystem = true
            },
            new FeatureDefinition
            {
                Code = FeatureCodes.ExportWord,
                Name = "Экспорт Word",
                Description = "Позволяет формировать Word-отчеты без фотографий.",
                Category = "Экспорт",
                ScopeCode = FeatureCatalogCodes.Scopes.Both,
                AudienceCode = FeatureCatalogCodes.Audiences.User,
                ValueType = FeatureValueType.Boolean,
                IsActive = true,
                IsSystem = true
            },
            new FeatureDefinition
            {
                Code = FeatureCodes.ExportWordWithPhotos,
                Name = "Экспорт Word с фото",
                Description = "Позволяет формировать Word-отчеты с фотографиями.",
                Category = "Экспорт",
                ScopeCode = FeatureCatalogCodes.Scopes.Both,
                AudienceCode = FeatureCatalogCodes.Audiences.User,
                ValueType = FeatureValueType.Boolean,
                IsActive = true,
                IsSystem = true
            },
            new FeatureDefinition
            {
                Code = FeatureCodes.ExportPdf,
                Name = "Экспорт PDF",
                Description = "Позволяет формировать PDF-отчеты без фотографий.",
                Category = "Экспорт",
                ScopeCode = FeatureCatalogCodes.Scopes.Both,
                AudienceCode = FeatureCatalogCodes.Audiences.User,
                ValueType = FeatureValueType.Boolean,
                IsActive = true,
                IsSystem = true
            },
            new FeatureDefinition
            {
                Code = FeatureCodes.ExportPdfWithPhotos,
                Name = "Экспорт PDF с фото",
                Description = "Позволяет формировать PDF-отчеты с фотографиями.",
                Category = "Экспорт",
                ScopeCode = FeatureCatalogCodes.Scopes.Both,
                AudienceCode = FeatureCatalogCodes.Audiences.User,
                ValueType = FeatureValueType.Boolean,
                IsActive = true,
                IsSystem = true
            },
            new FeatureDefinition
            {
                Code = FeatureCodes.VehiclePhotoAlbums,
                Name = "Альбомы фото техники",
                Description = "Позволяет вести галерею техники и отдельные альбомы с фотографиями.",
                Category = "Медиа",
                ScopeCode = FeatureCatalogCodes.Scopes.Both,
                AudienceCode = FeatureCatalogCodes.Audiences.User,
                ValueType = FeatureValueType.Boolean,
                IsActive = true,
                IsSystem = true
            },
            new FeatureDefinition
            {
                Code = FeatureCodes.ClientsModule,
                Name = "Клиенты",
                Description = "Открывает раздел клиентов в рабочем пространстве.",
                Category = "Модули",
                ScopeCode = FeatureCatalogCodes.Scopes.Organization,
                AudienceCode = FeatureCatalogCodes.Audiences.User,
                ValueType = FeatureValueType.Boolean,
                IsActive = true,
                IsSystem = true
            },
            new FeatureDefinition
            {
                Code = FeatureCodes.DefectsModule,
                Name = "Неисправности",
                Description = "Открывает раздел неисправностей в рабочем пространстве.",
                Category = "Модули",
                ScopeCode = FeatureCatalogCodes.Scopes.Organization,
                AudienceCode = FeatureCatalogCodes.Audiences.User,
                ValueType = FeatureValueType.Boolean,
                IsActive = true,
                IsSystem = true
            },
            new FeatureDefinition
            {
                Code = FeatureCodes.CountersModule,
                Name = "Пробег и наработка",
                Description = "Открывает раздел пробега и наработки в рабочем пространстве.",
                Category = "Модули",
                ScopeCode = FeatureCatalogCodes.Scopes.Organization,
                AudienceCode = FeatureCatalogCodes.Audiences.User,
                ValueType = FeatureValueType.Boolean,
                IsActive = true,
                IsSystem = true
            },
            new FeatureDefinition
            {
                Code = FeatureCodes.MaintenanceModule,
                Name = "Обслуживание",
                Description = "Открывает сценарии обслуживания, работ, истории ТО и ремонтов.",
                Category = "Модули",
                ScopeCode = FeatureCatalogCodes.Scopes.Organization,
                AudienceCode = FeatureCatalogCodes.Audiences.User,
                ValueType = FeatureValueType.Boolean,
                IsActive = true,
                IsSystem = true
            },
            new FeatureDefinition
            {
                Code = FeatureCodes.SchedulerModule,
                Name = "Планировщик",
                Description = "Открывает раздел планировщика в рабочем пространстве.",
                Category = "Модули",
                ScopeCode = FeatureCatalogCodes.Scopes.Organization,
                AudienceCode = FeatureCatalogCodes.Audiences.User,
                ValueType = FeatureValueType.Boolean,
                IsActive = true,
                IsSystem = true
            },
            new FeatureDefinition
            {
                Code = FeatureCodes.RemindersModule,
                Name = "Напоминания",
                Description = "Открывает раздел напоминаний в рабочем пространстве.",
                Category = "Модули",
                ScopeCode = FeatureCatalogCodes.Scopes.Organization,
                AudienceCode = FeatureCatalogCodes.Audiences.User,
                ValueType = FeatureValueType.Boolean,
                IsActive = true,
                IsSystem = true
            },
            new FeatureDefinition
            {
                Code = FeatureCodes.DocumentsModule,
                Name = "Документы",
                Description = "Открывает документы обслуживания, акты, заказ-наряды, чек-листы и отчеты.",
                Category = "Модули",
                ScopeCode = FeatureCatalogCodes.Scopes.Organization,
                AudienceCode = FeatureCatalogCodes.Audiences.User,
                ValueType = FeatureValueType.Boolean,
                IsActive = true,
                IsSystem = true
            },
            new FeatureDefinition
            {
                Code = FeatureCodes.MaterialsModule,
                Name = "Материалы и запчасти",
                Description = "Открывает раздел материалов и запчастей в рабочем пространстве.",
                Category = "Модули",
                ScopeCode = FeatureCatalogCodes.Scopes.Organization,
                AudienceCode = FeatureCatalogCodes.Audiences.User,
                ValueType = FeatureValueType.Boolean,
                IsActive = true,
                IsSystem = true
            },
            new FeatureDefinition
            {
                Code = FeatureCodes.WorkJournalModule,
                Name = "Журнал работ",
                Description = "Открывает общий журнал работ, событий обслуживания и изменений по технике.",
                Category = "Модули",
                ScopeCode = FeatureCatalogCodes.Scopes.Organization,
                AudienceCode = FeatureCatalogCodes.Audiences.User,
                ValueType = FeatureValueType.Boolean,
                IsActive = true,
                IsSystem = true
            },
            new FeatureDefinition
            {
                Code = FeatureCodes.FinanceModule,
                Name = "Финансы",
                Description = "Финансовая сводка: выручка, долги, себестоимость, прибыль, графики и заказы за период.",
                Category = "Модули",
                ScopeCode = FeatureCatalogCodes.Scopes.Organization,
                AudienceCode = FeatureCatalogCodes.Audiences.User,
                ValueType = FeatureValueType.Boolean,
                IsActive = true,
                IsSystem = true
            },
            new FeatureDefinition
            {
                Code = FeatureCodes.AnalyticsModule,
                Name = "Статистика",
                Description = "Открывает отдельный раздел статистики, графики, отчеты и сводные показатели рабочего пространства.",
                Category = "Модули",
                ScopeCode = FeatureCatalogCodes.Scopes.Organization,
                AudienceCode = FeatureCatalogCodes.Audiences.User,
                ValueType = FeatureValueType.Boolean,
                IsActive = true,
                IsSystem = true
            },
            new FeatureDefinition
            {
                Code = FeatureCodes.WikiModule,
                Name = "Вики организации",
                Description = "Открывает справку и инструкции по работе с организацией.",
                Category = "Модули",
                ScopeCode = FeatureCatalogCodes.Scopes.Organization,
                AudienceCode = FeatureCatalogCodes.Audiences.User,
                ValueType = FeatureValueType.Boolean,
                IsActive = true,
                IsSystem = true
            },
            new FeatureDefinition
            {
                Code = FeatureCodes.MembersModule,
                Name = "Участники",
                Description = "Открывает управление участниками, ролями и доступами организации.",
                Category = "Модули",
                ScopeCode = FeatureCatalogCodes.Scopes.Organization,
                AudienceCode = FeatureCatalogCodes.Audiences.Admin,
                ValueType = FeatureValueType.Boolean,
                IsActive = true,
                IsSystem = true
            },
            new FeatureDefinition
            {
                Code = FeatureCodes.BillingModule,
                Name = "Тарифы и оплата",
                Description = "Открывает раздел тарифов, подписки и оплаты рабочего пространства.",
                Category = "Модули",
                ScopeCode = FeatureCatalogCodes.Scopes.Organization,
                AudienceCode = FeatureCatalogCodes.Audiences.Admin,
                ValueType = FeatureValueType.Boolean,
                IsActive = true,
                IsSystem = true
            },
            new FeatureDefinition
            {
                Code = FeatureCodes.SettingsModule,
                Name = "Настройки организации",
                Description = "Открывает профиль, реквизиты, рабочие справочники и историю изменений организации.",
                Category = "Модули",
                ScopeCode = FeatureCatalogCodes.Scopes.Organization,
                AudienceCode = FeatureCatalogCodes.Audiences.Admin,
                ValueType = FeatureValueType.Boolean,
                IsActive = true,
                IsSystem = true
            }
        ];
    }
}
