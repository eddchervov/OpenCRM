using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Organizations.Contracts;
using Helpers;
using OpenCRM.Modules.Clients.Contracts;
using OpenCRM.Domain;
using OpenCRM.Domain.Assistant;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Features.Dtos;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Modules.Workspace.Shell;

/// <summary> Сервис оболочки рабочего пространства организации. </summary>
public sealed class OrganizationWorkspaceShellService(
    IOrganizationQueryService organizationService,
    IOrganizationEntitlementService organizationEntitlementService,
    IOrganizationPermissionService organizationPermissionService,
    IOrganizationClientCountQueryService clientCounts,
    IOrganizationWikiAvailability wikiAvailability,
    IPlatformCommercialPolicy commercialPolicy,
    OpenCRMContext context) : IOrganizationWorkspaceShellService
{
    /// <summary> Возвращает оболочку рабочего пространства для пользователя и организации. </summary>
    public async Task<OrganizationWorkspaceShellDto> GetAsync(string userId, Guid organizationId)
    {
        var organization = await organizationService.GetByIdAsync(userId, organizationId);
        var featureSnapshot = await organizationEntitlementService.GetFeatureSnapshotAsync(userId, organizationId);
        var profile = BuildScenarioProfile(organization.PrimaryScenarioCode, featureSnapshot);
        var assistantVisible = await context.Set<AssistantSettings>().AsNoTracking()
            .AnyAsync(settings => settings.Id == 1 && settings.IsEnabled && settings.WebEnabled);
        var enabledFeatures = featureSnapshot.Features.Where(feature => feature.BoolValue == true)
            .Select(feature => feature.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hasWikiContent = wikiAvailability.HasContent;
        var wikiFeatureAvailable = !commercialPolicy.CommercialEnabled
            || enabledFeatures.Contains(FeatureCodes.WikiModule);
        profile = profile with
        {
            Modules = profile.Modules with
            {
                AssistantVisible = assistantVisible,
                Wiki = hasWikiContent && wikiFeatureAvailable,
                Billing = commercialPolicy.CommercialEnabled && profile.Modules.Billing
            }
        };
        var menu = await this.BuildMenuAsync(userId, organization.Id, profile, hasWikiContent);

        return new OrganizationWorkspaceShellDto
        {
            Organization = organization,
            Scenario = profile,
            Menu = menu.Select(item => item with
            {
                IsAvailable = item.RequiredFeatureCode is null || enabledFeatures.Contains(item.RequiredFeatureCode),
                UnavailableMessage = item.RequiredFeatureCode is not null && !enabledFeatures.Contains(item.RequiredFeatureCode)
                    ? commercialPolicy.CommercialEnabled ? "Доступно в PRO" : "Функция отключена в настройках установки"
                    : null
            }).ToList(),
            QuickActions = await BuildQuickActionsAsync(userId, organization.Id, profile)
        };
    }

    /// <summary> Строит профиль сценария рабочего пространства по коду и доступным возможностям. </summary>
    private static OrganizationWorkspaceScenarioProfileDto BuildScenarioProfile(
        string? sourceScenarioCode,
        FeatureAccessSnapshotDto featureSnapshot)
    {
        var sourceCode = NormalizeCode(sourceScenarioCode);
        var enabledFeatures = featureSnapshot.Features
            .Where(x => x.BoolValue == true)
            .Select(x => x.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var modules = new OrganizationWorkspaceScenarioModulesDto
        {
            Intake = enabledFeatures.Contains(FeatureCodes.MaintenanceIntake),
            Assistant = enabledFeatures.Contains(FeatureCodes.Assistant),
            Messaging = enabledFeatures.Contains(FeatureCodes.ClientMessaging),
            Photos = enabledFeatures.Contains(FeatureCodes.VehiclePhotoAlbums),
            Vehicles = enabledFeatures.Contains(FeatureCodes.VehiclesModule),
            Clients = enabledFeatures.Contains(FeatureCodes.ClientsModule),
            Maintenance = enabledFeatures.Contains(FeatureCodes.MaintenanceModule)
                || enabledFeatures.Contains(FeatureCodes.VehiclesModule),
            Defects = enabledFeatures.Contains(FeatureCodes.DefectsModule),
            Counters = enabledFeatures.Contains(FeatureCodes.CountersModule),
            Scheduler = enabledFeatures.Contains(FeatureCodes.SchedulerModule),
            Materials = enabledFeatures.Contains(FeatureCodes.MaterialsModule),
            Documents = enabledFeatures.Contains(FeatureCodes.DocumentsModule),
            WorkJournal = enabledFeatures.Contains(FeatureCodes.WorkJournalModule),
            Finance = enabledFeatures.Contains(FeatureCodes.FinanceModule),
            AccountingExport = enabledFeatures.Contains(FeatureCodes.FinanceExport1C),
            Analytics = enabledFeatures.Contains(FeatureCodes.AnalyticsModule),
            Wiki = enabledFeatures.Contains(FeatureCodes.WikiModule),
            Members = enabledFeatures.Contains(FeatureCodes.MembersModule),
            Billing = enabledFeatures.Contains(FeatureCodes.BillingModule),
            Settings = enabledFeatures.Contains(FeatureCodes.SettingsModule)
        };

        var profile = sourceCode switch
        {
            OrganizationTypeCodes.Detailing => new OrganizationWorkspaceScenarioProfileDto
            {
                Code = sourceCode,
                SourceCode = sourceCode,
                Title = "Детейлинг",
                Summary = "Рабочий день детейлинга: клиенты, авто, записи, услуги, фото и история визитов.",
                Terms = new OrganizationWorkspaceScenarioTermsDto
                {
                    Vehicle = "Авто",
                    Vehicles = "Автомобили",
                    Maintenance = "Услуга",
                    Maintenances = "История услуг",
                    Counters = "Пробег",
                    Scheduler = "Записи",
                    Materials = "Склад",
                    WorkJournal = "Журнал услуг"
                },
                Modules = modules,
                MaintenanceForm = new OrganizationWorkspaceMaintenanceFormProfileDto
                {
                    Mode = "quick-service",
                    PreferQuickVisitForm = true,
                    ShowMileage = false,
                    ShowMaterials = modules.Materials,
                    ShowLinkedDefects = false,
                    ShowPhotos = modules.Photos,
                    ShowDocuments = modules.Documents,
                    ShowPerformer = modules.Members,
                    ShowLocation = true
                }
            },
            OrganizationTypeCodes.Wrapping => BuildWrappingProfile(sourceCode, modules),
            OrganizationTypeCodes.Fleet => BuildFleetProfile(sourceCode, modules),
            OrganizationTypeCodes.DrivingSchool => BuildDrivingSchoolProfile(sourceCode, modules),
            _ => BuildServiceStationProfile(sourceCode, sourceCode, modules)
        };

        return EnrichScenarioProfile(profile);
    }

    /// <summary> Дополняет профиль сценария дополнительными данными. </summary>
    private static OrganizationWorkspaceScenarioProfileDto EnrichScenarioProfile(
        OrganizationWorkspaceScenarioProfileDto profile)
    {
        var code = NormalizeCode(profile.Code);
        var process = OrganizationScenarioProcessCatalog.Resolve(code);

        return profile with
        {
            Code = code,
            Process = process,
            RecommendedModules = BuildRecommendedModules(code, profile.Terms),
            Focus = BuildFocus(code),
            Capabilities = BuildCapabilities(code),
            DashboardHint = string.Empty,
            Metrics = BuildMetrics(code)
        };
    }

    /// <summary> Строит профиль сценария для СТО. </summary>
    private static OrganizationWorkspaceScenarioProfileDto BuildServiceStationProfile(
        string code,
        string sourceCode,
        OrganizationWorkspaceScenarioModulesDto modules) =>
        new()
        {
            Code = code,
            SourceCode = sourceCode,
            Title = "Автосервис",
            Summary = "Приемка клиентов, автомобили, заказ-наряды, посты, склад запчастей и выдача.",
            Terms = new OrganizationWorkspaceScenarioTermsDto
            {
                Vehicle = "Авто",
                Vehicles = "Автомобили",
                Maintenance = "Заказ-наряд",
                Maintenances = "Заказ-наряды",
                Defect = "Замечание",
                Defects = "Диагностика",
                Counters = "Пробег",
                Scheduler = "Записи",
                Materials = "Склад запчастей",
                WorkJournal = "История работ"
            },
            Modules = modules,
            MaintenanceForm = new OrganizationWorkspaceMaintenanceFormProfileDto
            {
                Mode = "service-station",
                PreferQuickVisitForm = true,
                ShowMileage = modules.Counters,
                ShowMaterials = modules.Materials,
                ShowLinkedDefects = modules.Defects,
                ShowPhotos = modules.Photos,
                ShowDocuments = modules.Documents,
                ShowPerformer = modules.Members,
                ShowLocation = true
            }
        };

    // HIDDEN_FLEET: профиль нужен прежним организациям; он не возвращает сценарий в выбор создания. См. docs/hidden-fleet-scenario.md.
    /// <summary> Строит профиль сценария для автопарка. </summary>
    private static OrganizationWorkspaceScenarioProfileDto BuildFleetProfile(
        string sourceCode,
        OrganizationWorkspaceScenarioModulesDto modules) =>
        BuildServiceStationProfile(OrganizationTypeCodes.Fleet, sourceCode, modules) with
        {
            Title = "Автопарк",
            Summary = "Регламентное обслуживание собственной техники, пробег, планирование и контроль состояния.",
            Terms = new OrganizationWorkspaceScenarioTermsDto
            {
                Vehicle = "Техника",
                Vehicles = "Активная техника",
                Maintenance = "ТО",
                Maintenances = "Обслуживание",
                Defect = "Неисправность",
                Defects = "Неисправности"
            }
        };

    /// <summary> Строит профиль сценария для детейлинга и обёртывания. </summary>
    private static OrganizationWorkspaceScenarioProfileDto BuildWrappingProfile(
        string sourceCode,
        OrganizationWorkspaceScenarioModulesDto modules) =>
        BuildServiceStationProfile(OrganizationTypeCodes.Wrapping, sourceCode, modules) with
        {
            Title = "Оклейка и тюнинг",
            Summary = "Клиенты, проекты, этапы работ, материалы, замечания и выдача.",
            Terms = new OrganizationWorkspaceScenarioTermsDto
            {
                Vehicle = "Авто",
                Vehicles = "Проекты и авто",
                Client = "Заказчик",
                Clients = "Заказчики",
                Maintenance = "Проект",
                Maintenances = "Проекты",
                Defect = "Замечание",
                Defects = "Замечания",
                Counters = "Пробег",
                Scheduler = "Этапы и записи",
                Materials = "Материалы",
                WorkJournal = "Журнал проектов"
            },
            MaintenanceForm = new OrganizationWorkspaceMaintenanceFormProfileDto
            {
                Mode = "project",
                PreferQuickVisitForm = false,
                ShowMileage = false,
                ShowMaterials = modules.Materials,
                ShowLinkedDefects = modules.Defects,
                ShowPhotos = true,
                ShowDocuments = modules.Documents,
                ShowPerformer = true,
                ShowLocation = true
            }
        };

    /// <summary> Строит профиль сценария для автошколы. </summary>
    private static OrganizationWorkspaceScenarioProfileDto BuildDrivingSchoolProfile(
        string sourceCode,
        OrganizationWorkspaceScenarioModulesDto modules) =>
        BuildServiceStationProfile(OrganizationTypeCodes.DrivingSchool, sourceCode, modules) with
        {
            Title = "Автошкола",
            Summary = "Учебная техника, плановые работы, пробег, ответственные и история обслуживания.",
            Terms = new OrganizationWorkspaceScenarioTermsDto
            {
                Vehicle = "Учебная техника",
                Vehicles = "Учебная техника",
                Maintenance = "Работа",
                Maintenances = "Обслуживание",
                Defect = "Неисправность",
                Defects = "Неисправности"
            }
        };

    /// <summary> Формирует пункты меню рабочего пространства с учётом прав доступа. </summary>
    private async Task<IReadOnlyList<OrganizationWorkspaceMenuItemDto>> BuildMenuAsync(
        string userId,
        Guid organizationId,
        OrganizationWorkspaceScenarioProfileDto profile,
        bool hasWikiContent)
    {
        var canViewVehicles = await organizationPermissionService.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.VehiclesView);
        var canViewClients = await organizationPermissionService.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.ClientsView);
        var canViewInquiries = await organizationPermissionService.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.InquiriesView);
        var canViewMessages = await organizationPermissionService.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.MessagesView);
        var clientsCount = canViewClients
            ? await clientCounts.GetActiveCountAsync(organizationId)
            : (int?)null;
        var canViewDefects = await organizationPermissionService.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.DefectsView);
        var canViewCounters = await organizationPermissionService.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.CountersView);
        var canViewScheduler = await organizationPermissionService.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.SchedulerView);
        var canViewMaterials = await organizationPermissionService.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.MaterialsView);
        var canViewMaintenance = await organizationPermissionService.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.MaintenanceView);
        var canViewOwnWork = await organizationPermissionService.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.MaintenanceWorkView)
            && await context.OrganizationEmployees.AsNoTracking()
                .AnyAsync(employee => employee.OrganizationId == organizationId && employee.IsActive
                    && employee.Member != null && employee.Member.UserId == userId);
        var canViewEmployees = await organizationPermissionService.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.EmployeesManage)
            || await organizationPermissionService.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.MaintenanceEdit);

        var canViewShifts = await organizationPermissionService.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.ShiftsView);
        var canViewWorkJournal = await organizationPermissionService.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.WorkJournalView);
        var canViewFinance = (await organizationPermissionService.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.FinanceView)
                || await organizationPermissionService.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.FinanceManage));
        var canViewAnalytics = await organizationPermissionService.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.AnalyticsView);
        var canViewWiki = hasWikiContent
            && await organizationPermissionService.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.WikiView);
        var canViewMembers = await organizationPermissionService.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.MembersView);
        var canViewBilling = commercialPolicy.CommercialEnabled
            && await organizationPermissionService.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.BillingManage);
        var canViewCatalog = (await organizationPermissionService.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.WorkspaceCatalogView)
                || (canViewMessages && await organizationPermissionService.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.MessagesTemplatesManage)));
        var canViewSettings = (await organizationPermissionService.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.WorkspaceProfileEdit)
                || await organizationPermissionService.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.WorkspaceRequisitesEdit)
                || await organizationPermissionService.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.WorkspaceArchive));
        var isDetailing = profile.Code == OrganizationTypeCodes.Detailing;
        var isServiceStation = profile.Code == OrganizationTypeCodes.ServiceStation;
        var commonItems = BuildCommonMenuItems(
            canViewOwnWork,
            canViewEmployees,
            canViewShifts,
            canViewWiki,
            commercialPolicy.CommercialEnabled ? FeatureCodes.WikiModule : null,
            canViewMembers,
            canViewBilling,
            canViewCatalog,
            canViewSettings,
            isDetailing || isServiceStation);

        if (isDetailing)
        {
            return new List<OrganizationWorkspaceMenuItemDto>
            {
                MenuItem("workspace", ["workspace"], "Рабочий день", "#workspace", "fa fa-home", null, canViewMaintenance, "Ежедневная работа"),
                MenuItem("scheduler", ["scheduler"], profile.Terms.Scheduler, "#scheduler", "fa fa-calendar", FeatureCodes.SchedulerModule, canViewScheduler, "Ежедневная работа"),
                MenuItem("work-journal", ["work-journal"], profile.Terms.WorkJournal, "#work-journal", "fa fa-list-alt", FeatureCodes.WorkJournalModule, canViewWorkJournal && (canViewMaintenance || canViewDefects), "Ежедневная работа"),
                MenuItem("visualization", ["visualization"], "Визуализация", "#visualization", "fa fa-cubes", null, canViewMaintenance && canViewCatalog, "Ежедневная работа"),
                MenuItem("messages", ["messages"], "Сообщения", "#messages", "fa fa-commenting-o", FeatureCodes.ClientMessaging, canViewMessages, "Клиенты"),
                MenuItem("inquiries", ["inquiries"], "Обращения", "#inquiries", "fa fa-comments-o", FeatureCodes.ClientsModule, canViewInquiries, "Клиенты"),
                MenuItem("clients", ["clients"], profile.Terms.Clients, "#clients", "fa fa-users", FeatureCodes.ClientsModule, canViewClients, "Клиенты", count: clientsCount),
                MenuItem("vehicles", ["vehicles", "vehicle", "maintenance"], profile.Terms.Vehicles, "#vehicles", "fa fa-car", FeatureCodes.VehiclesModule, canViewVehicles, "Клиенты"),
                MenuItem("materials", ["materials"], profile.Terms.Materials, "#materials", "fa fa-archive", FeatureCodes.MaterialsModule, canViewMaterials, "Ресурсы"),
                MenuItem("finance", ["finance"], "Финансы", "#finance", "fa fa-rub", FeatureCodes.FinanceModule, canViewFinance, "Отчёты"),
                MenuItem("statistics", ["statistics"], "Статистика", "#statistics", "fa fa-bar-chart", FeatureCodes.AnalyticsModule, canViewAnalytics, "Отчёты")
            }.Concat(commonItems)
                .OrderBy(item => item.GroupTitle switch
                {
                    "Ежедневная работа" => 0,
                    "Клиенты" => 1,
                    "Ресурсы" => 2,
                    "Отчёты" => 3,
                    _ => 4
                })
                .ToList();
        }

        if (isServiceStation)
        {
            return new List<OrganizationWorkspaceMenuItemDto>
            {
                MenuItem("workspace", ["workspace"], "Рабочий день", "#workspace", "fa fa-home", null, canViewMaintenance, "Ежедневная работа"),
                MenuItem("scheduler", ["scheduler"], profile.Terms.Scheduler, "#scheduler", "fa fa-calendar", FeatureCodes.SchedulerModule, canViewScheduler, "Ежедневная работа"),
                MenuItem("work-journal", ["work-journal"], profile.Terms.WorkJournal, "#work-journal", "fa fa-list-alt", FeatureCodes.WorkJournalModule, canViewWorkJournal && (canViewMaintenance || canViewDefects), "Ежедневная работа"),
                MenuItem("visualization", ["visualization"], "Визуализация", "#visualization", "fa fa-cubes", null, canViewMaintenance && canViewCatalog, "Ежедневная работа"),
                MenuItem("messages", ["messages"], "Сообщения", "#messages", "fa fa-commenting-o", FeatureCodes.ClientMessaging, canViewMessages, "Клиенты"),
                MenuItem("inquiries", ["inquiries"], "Обращения", "#inquiries", "fa fa-comments-o", FeatureCodes.ClientsModule, canViewInquiries, "Клиенты"),
                MenuItem("clients", ["clients"], profile.Terms.Clients, "#clients", "fa fa-users", FeatureCodes.ClientsModule, canViewClients, "Клиенты", count: clientsCount),
                MenuItem("vehicles", ["vehicles", "vehicle"], profile.Terms.Vehicles, "#vehicles", "fa fa-car", FeatureCodes.VehiclesModule, canViewVehicles, "Клиенты"),
                MenuItem("materials", ["materials"], profile.Terms.Materials, "#materials", "fa fa-archive", FeatureCodes.MaterialsModule, canViewMaterials, "Ресурсы"),
                MenuItem("finance", ["finance"], "Финансы", "#finance", "fa fa-rub", FeatureCodes.FinanceModule, canViewFinance, "Отчёты"),
                MenuItem("statistics", ["statistics"], "Статистика", "#statistics", "fa fa-bar-chart", FeatureCodes.AnalyticsModule, canViewAnalytics, "Отчёты")
            }.Concat(commonItems)
                .OrderBy(item => item.GroupTitle switch
                {
                    "Ежедневная работа" => 0,
                    "Клиенты" => 1,
                    "Ресурсы" => 2,
                    "Отчёты" => 3,
                    _ => 4
                })
                .ToList();
        }

        return new List<OrganizationWorkspaceMenuItemDto>
        {
            MenuItem("workspace", ["workspace"], "Рабочее пространство", "#workspace", "fa fa-home", null, canViewMaintenance, "Основное"),
            MenuItem("visualization", ["visualization"], "Визуализация", "#visualization", "fa fa-cubes", null, canViewMaintenance && canViewCatalog, "Основное"),
            MenuItem("vehicles", ["vehicles", "vehicle", "maintenance"], profile.Terms.Vehicles, "#vehicles", "fa fa-car", FeatureCodes.VehiclesModule, canViewVehicles, "Работа"),
            MenuItem("clients", ["clients"], profile.Terms.Clients, "#clients", "fa fa-users", FeatureCodes.ClientsModule, canViewClients, "Работа", count: clientsCount),
            MenuItem("defects", ["defects"], profile.Terms.Defects, "#defects", "fa fa-exclamation-triangle", FeatureCodes.DefectsModule, canViewDefects, "Работа"),
            MenuItem("counters", ["counters"], profile.Terms.Counters, "#counters", "fa fa-tachometer", FeatureCodes.CountersModule, canViewCounters, "Работа"),
            MenuItem("scheduler", ["scheduler"], profile.Terms.Scheduler, "#scheduler", "fa fa-calendar", FeatureCodes.SchedulerModule, canViewScheduler, "Работа"),
            MenuItem("materials", ["materials"], profile.Terms.Materials, "#materials", "fa fa-archive", FeatureCodes.MaterialsModule, canViewMaterials, "Работа"),
            MenuItem("work-journal", ["work-journal"], profile.Terms.WorkJournal, "#work-journal", "fa fa-list-alt", FeatureCodes.WorkJournalModule, canViewWorkJournal && (canViewMaintenance || canViewDefects), "Работа"),
            MenuItem("finance", ["finance"], "Финансы", "#finance", "fa fa-rub", FeatureCodes.FinanceModule, canViewFinance, "Отчеты"),
            MenuItem("statistics", ["statistics"], "Статистика", "#statistics", "fa fa-bar-chart", FeatureCodes.AnalyticsModule, canViewAnalytics, "Отчеты")
        }.Concat(commonItems).ToList();
    }

    /// <summary> Формирует быстрые действия рабочего пространства. </summary>
    private async Task<IReadOnlyList<OrganizationWorkspaceQuickActionDto>> BuildQuickActionsAsync(
        string userId,
        Guid organizationId,
        OrganizationWorkspaceScenarioProfileDto profile)
    {
        var createMaintenanceTitle = profile.Code switch
        {
            OrganizationTypeCodes.Detailing => "Записать услугу",
            OrganizationTypeCodes.ServiceStation => "Новый заказ-наряд",
            _ => "Создать обслуживание"
        };

        var canCreateMaintenance = profile.Modules.Maintenance
            && await organizationPermissionService.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.MaintenanceCreate);
        var canCreateClient = profile.Modules.Clients
            && await organizationPermissionService.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.ClientsCreate);
        var canCreateVehicle = profile.Modules.Vehicles
            && await organizationPermissionService.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.VehiclesCreate);

        return
        [
            QuickAction("create-maintenance", createMaintenanceTitle, "#maintenance/new", "fa fa-calendar-plus-o", "maintenance-create", FeatureCodes.MaintenanceModule, canCreateMaintenance, true),
            QuickAction("create-client", "Добавить клиента", "#clients/new", "fa fa-user-plus", "client-create", FeatureCodes.ClientsModule, canCreateClient),
            QuickAction("create-vehicle", $"Добавить {profile.Terms.Vehicle.ToLowerInvariant()}", "#vehicles/new", "fa fa-car", "vehicle-create", FeatureCodes.VehiclesModule, canCreateVehicle)
        ];
    }

    /// <summary> Формирует общие пункты меню по правам доступа. </summary>
    private static IReadOnlyList<OrganizationWorkspaceMenuItemDto> BuildCommonMenuItems(
        bool canViewOwnWork,
        bool canViewEmployees,
        bool canViewShifts,
        bool canViewWiki,
        string? wikiRequiredFeatureCode,
        bool canViewMembers,
        bool canViewBilling,
        bool canViewCatalog,
        bool canViewSettings,
        bool useCrmNavigation) =>
        [
            MenuItem("my-work", ["my-work"], "Мои работы", "#my-work", "fa fa-wrench", FeatureCodes.MembersModule, canViewOwnWork, useCrmNavigation ? "Ежедневная работа" : "Работа"),
            MenuItem("wiki", ["wiki"], "Вики", "#wiki", "fa fa-book", wikiRequiredFeatureCode, canViewWiki, "Справка и доступ"),
            MenuItem("employees", ["employees", "members"], "Команда", "#employees", "fa fa-users", FeatureCodes.MembersModule, canViewEmployees || canViewMembers, useCrmNavigation ? "Ресурсы" : "Работа"),
            MenuItem("shifts", ["shifts"], "Смены", "#shifts", "fa fa-calendar", FeatureCodes.MembersModule, canViewShifts, useCrmNavigation ? "Ресурсы" : "Работа"),
            MenuItem("members", ["members"], "Команда · доступ", "#members", "fa fa-users", FeatureCodes.MembersModule, canViewMembers, useCrmNavigation ? "Ресурсы" : "Работа"),
            MenuItem("billing", ["billing"], "Тарифы", "#billing", "fa fa-credit-card", FeatureCodes.BillingModule, canViewBilling, "Справка и доступ"),
            MenuItem("catalog", ["catalog"], "Справочники", "#catalog", "fa fa-list", FeatureCodes.SettingsModule, canViewCatalog, useCrmNavigation ? "Ресурсы" : "Работа"),
            MenuItem("settings", ["settings"], "Организация", "#settings", "fa fa-sliders", FeatureCodes.SettingsModule, canViewSettings, "Справка и доступ")
        ];

    /// <summary> Создаёт пункт меню с заданными параметрами. </summary>
    private static OrganizationWorkspaceMenuItemDto MenuItem(
        string code,
        IReadOnlyList<string> sections,
        string title,
        string url,
        string icon,
        string? requiredFeatureCode,
        bool isVisible,
        string groupTitle,
        string? sectionLabel = null,
        int? count = null,
        bool hasDividerBefore = false) =>
        new()
        {
            Code = code,
            SectionCodes = sections,
            Title = title,
            UrlTemplate = url,
            IconCssClass = icon,
            RequiredFeatureCode = requiredFeatureCode,
            GroupTitle = groupTitle,
            SectionLabel = sectionLabel,
            Count = count,
            HasDividerBefore = hasDividerBefore,
            IsVisible = isVisible
        };

    /// <summary> Создаёт быстрое действие с заданными параметрами. </summary>
    private static OrganizationWorkspaceQuickActionDto QuickAction(
        string code,
        string title,
        string url,
        string icon,
        string formCode,
        string? requiredFeatureCode,
        bool isVisible,
        bool isPrimary = false) =>
        new()
        {
            Code = code,
            Title = title,
            UrlTemplate = url,
            IconCssClass = icon,
            FormCode = formCode,
            RequiredFeatureCode = requiredFeatureCode,
            IsVisible = isVisible,
            IsPrimary = isPrimary
        };

    /// <summary> Формирует список рекомендуемых модулей по коду сценария. </summary>
    private static IReadOnlyList<string> BuildRecommendedModules(
        string code,
        OrganizationWorkspaceScenarioTermsDto terms) => code switch
    {
        OrganizationTypeCodes.Detailing =>
        [
            terms.Clients,
            terms.Vehicles,
            terms.Scheduler,
            terms.Materials
        ],
        OrganizationTypeCodes.Wrapping =>
        [
            terms.Clients,
            terms.Vehicles,
            terms.Maintenances,
            terms.Scheduler,
            terms.Materials
        ],
        OrganizationTypeCodes.Fleet =>
        [
            terms.Vehicles,
            terms.Counters,
            terms.Maintenances,
            terms.Defects,
            terms.Scheduler
        ],
        OrganizationTypeCodes.DrivingSchool =>
        [
            terms.Vehicles,
            terms.Counters,
            terms.Maintenances,
            terms.Scheduler
        ],
        _ =>
        [
            terms.Clients,
            terms.Vehicles,
            terms.Maintenances,
            terms.Defects,
            terms.Scheduler
        ]
    };

    /// <summary> Формирует список акцентов сценария по его коду. </summary>
    private static IReadOnlyList<string> BuildFocus(string code) => code switch
    {
        OrganizationTypeCodes.Detailing =>
        [
            "Записи на сегодня",
            "Авто в работе",
            "Фото осмотра и результата"
        ],
        OrganizationTypeCodes.Wrapping =>
        [
            "Этапы заказа",
            "Материалы",
            "Коммуникация с клиентом"
        ],
        OrganizationTypeCodes.Fleet =>
        [
            "Плановое ТО",
            "Пробег/моточасы",
            "Приоритеты ремонта"
        ],
        OrganizationTypeCodes.DrivingSchool =>
        [
            "Готовность машин",
            "Регламент обслуживания",
            "Ответственные сотрудники"
        ],
        _ =>
        [
            "Быстрый прием техники",
            "История работ по клиенту",
            "Контроль открытых проблем"
        ]
    };

    /// <summary> Формирует список возможностей сценария по его коду. </summary>
    private static IReadOnlyList<string> BuildCapabilities(string code) => code switch
    {
        OrganizationTypeCodes.Detailing =>
        [
            "Вести базу клиентов",
            "Быстро записывать авто на услугу",
            "Видеть рабочий день по времени",
            "Хранить фото и историю услуг",
            "Учитывать материалы детейлинга"
        ],
        OrganizationTypeCodes.Wrapping =>
        [
            "Вести проекты оклейки",
            "Разбивать работу на этапы",
            "Планировать выдачу",
            "Учитывать пленку и материалы",
            "Формировать документы по проекту"
        ],
        OrganizationTypeCodes.Fleet =>
        [
            "Контролировать парк техники",
            "Вести пробег и моточасы",
            "Планировать ТО",
            "Фиксировать ремонтные заявки",
            "Смотреть готовность техники к линии"
        ],
        OrganizationTypeCodes.DrivingSchool =>
        [
            "Вести учебные автомобили",
            "Контролировать готовность к занятиям",
            "Планировать регламентные работы",
            "Назначать ответственных",
            "Хранить документы по технике"
        ],
        _ =>
        [
            "Вести клиентов и автомобили",
            "Создавать заказы и обслуживания",
            "Формировать заказ-наряды и акты",
            "Планировать работы мастеров",
            "Учитывать материалы и запчасти"
        ]
    };

    /// <summary> Формирует список метрик сценария по его коду. </summary>
    private static IReadOnlyList<OrganizationWorkspaceScenarioMetricDto> BuildMetrics(string code) => code switch
    {
        OrganizationTypeCodes.Detailing =>
        [
            Metric("maintenance.planned", "Записи", "#scheduler", FeatureCodes.SchedulerModule),
            Metric("maintenance.inProgress", "В работе", "#maintenance", FeatureCodes.VehiclesModule),
            Metric("clients.total", "Клиенты", "#clients", FeatureCodes.ClientsModule),
            Metric("materials.low", "Материалы", "#materials", FeatureCodes.MaterialsModule)
        ],
        OrganizationTypeCodes.Wrapping =>
        [
            Metric("maintenance.planned", "Запланировано", "#scheduler", FeatureCodes.SchedulerModule),
            Metric("maintenance.inProgress", "В работе", "#maintenance", FeatureCodes.VehiclesModule),
            Metric("clients.total", "Клиенты", "#clients", FeatureCodes.ClientsModule),
            Metric("documents.total", "Документы", "#work-journal", null)
        ],
        OrganizationTypeCodes.Fleet =>
        [
            Metric("vehicles.active", "Активная техника", "#vehicles", FeatureCodes.VehiclesModule),
            Metric("planner.overdue", "Просрочено", "#scheduler?quick=overdue", FeatureCodes.SchedulerModule),
            Metric("defects.open", "Открытые неисправности", "#defects", FeatureCodes.DefectsModule),
            Metric("counters.entries", "Записи пробега", "#counters", FeatureCodes.CountersModule)
        ],
        OrganizationTypeCodes.DrivingSchool =>
        [
            Metric("vehicles.active", "Учебная техника", "#vehicles", FeatureCodes.VehiclesModule),
            Metric("maintenance.planned", "Плановые работы", "#scheduler", FeatureCodes.SchedulerModule),
            Metric("planner.soon", "Скоро", "#scheduler?quick=soon", FeatureCodes.SchedulerModule),
            Metric("counters.entries", "Пробег", "#counters", FeatureCodes.CountersModule)
        ],
        _ =>
        [
            Metric("clients.total", "Клиенты", "#clients", FeatureCodes.ClientsModule),
            Metric("maintenance.inProgress", "В работе", "#maintenance", FeatureCodes.VehiclesModule),
            Metric("defects.open", "Открытые неисправности", "#defects", FeatureCodes.DefectsModule),
            Metric("planner.overdue", "Просрочено", "#scheduler?quick=overdue", FeatureCodes.SchedulerModule)
        ]
    };

    /// <summary> Создаёт метрику сценария с заданными параметрами. </summary>
    private static OrganizationWorkspaceScenarioMetricDto Metric(
        string code,
        string title,
        string urlTemplate,
        string? requiredFeatureCode) =>
        new()
        {
            Code = code,
            Title = title,
            UrlTemplate = urlTemplate,
            RequiredFeatureCode = requiredFeatureCode
        };

    /// <summary> Нормализует код сценария. </summary>
    private static string NormalizeCode(string? code)
    {
        return string.IsNullOrWhiteSpace(code)
            ? OrganizationTypeCodes.ServiceStation
            : code.Trim().ToLowerInvariant();
    }
}
