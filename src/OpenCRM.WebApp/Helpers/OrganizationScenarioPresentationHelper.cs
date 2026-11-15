using Helpers;
using OpenCRM.Domain;
using OpenCRM.Services.Interfaces.Features.Dtos;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.WebApp.Presentation;

/// <summary>
/// UI-пресет типа организации: краткое описание, модули, преимущества, метрики и быстрые действия.
/// </summary>
public sealed record OrganizationScenarioPresentation(
    string Code,
    string Title,
    string Summary,
    IReadOnlyList<string> Modules,
    IReadOnlyList<string> Focus,
    IReadOnlyList<string> Capabilities,
    string DashboardHint,
    IReadOnlyList<OrganizationScenarioMetric> Metrics,
    IReadOnlyList<OrganizationScenarioAction> Actions);

/// <summary>
/// Метрика сценарного dashboard организации.
/// </summary>
public sealed record OrganizationScenarioMetric(
    string Code,
    string Title,
    string UrlTemplate,
    string? RequiredFeatureCode);

/// <summary>
/// Быстрое действие сценарного dashboard организации.
/// </summary>
public sealed record OrganizationScenarioAction(
    string Title,
    string UrlTemplate,
    string IconCssClass,
    string? RequiredFeatureCode,
    bool IsPrimary = false);

/// <summary>
/// Терминология интерфейса под тип организации.
/// </summary>
public sealed record OrganizationScenarioTerms(
    string WorkspaceTitle,
    string VehiclesTitle,
    string VehicleSingular,
    string ClientsTitle,
    string ClientSingular,
    string MaintenanceTitle,
    string MaintenanceSingular,
    string DefectsTitle,
    string DefectSingular,
    string CountersTitle,
    string SchedulerTitle,
    string WorkJournalTitle,
    string CreateMaintenanceText,
    string CreateDefectText,
    string AddVehicleText,
    string EmptyVehiclesTitle,
    string EmptyVehiclesText);

/// <summary>
/// UX-профиль сценария: какие блоки формы показывать по умолчанию и какими словами называть запись.
/// </summary>
public sealed record OrganizationScenarioUxProfile(
    string ComplexityCode,
    bool PreferQuickVisitForm,
    bool ShowMileageByDefault,
    bool ShowMaterialsByDefault,
    bool ShowDefectsByDefault,
    bool ShowPhotosByDefault,
    string CreateTitle,
    string EditTitle,
    string SubtitlePrefix,
    string LoadingText,
    string SaveButtonText,
    string StatusLabel,
    string TypeLabel,
    string PlannedDateLabel,
    string PlannedStartTimeLabel,
    string PlannedEndTimeLabel,
    string CompletedDateLabel,
    string MileageLabel,
    string LocationLabel,
    string LocationPlaceholder,
    string PerformerLabel,
    string PerformerPlaceholder,
    string PerformerHelpText,
    string WorksTitle,
    string DetailNameLabel,
    string DetailNamePlaceholder,
    string DetailDescriptionPlaceholder,
    string AddDetailText,
    string CommentPlaceholder);

/// <summary>
/// Presentation-модель рабочего процесса организации под выбранный сценарий.
/// </summary>
public sealed record OrganizationScenarioProcess(
    string WorkPipelineTitle,
    IReadOnlyList<string> WorkStages,
    IReadOnlyList<string> DocumentTemplates,
    IReadOnlyList<string> PlanningRules,
    string NewWorkflowTitle,
    string InProgressWorkflowTitle,
    string DeferredWorkflowTitle,
    string ClosedWorkflowTitle,
    string StartActionText,
    string CloseActionText,
    string DeferActionText,
    string DefectSourceTitle,
    string DateSourceTitle,
    string MileageSourceTitle,
    string CounterSourceTitle,
    string SetupSourceTitle);

/// <summary>
/// Собирает русскую терминологию, преимущества, метрики и действия для разных типов организаций.
/// </summary>
public static class OrganizationScenarioPresentationHelper
{
    /// <summary> Терминология сценария по умолчанию. </summary>
    private static readonly OrganizationScenarioTerms DefaultTerms = new(
        "Рабочее пространство",
        "Техника",
        "Техника",
        "Клиенты",
        "Клиент",
        "Обслуживания",
        "Обслуживание",
        "Неисправности",
        "Неисправность",
        "Пробег и наработка",
        "Планирование",
        "Журнал работ",
        "Создать обслуживание",
        "Создать неисправность",
        "Добавить технику",
        "Техника пока не добавлена",
        "Добавьте первую единицу техники, чтобы вести обслуживание, расходы и историю.");

    /// <summary> Наборы терминов по кодам сценариев. </summary>
    private static readonly IReadOnlyDictionary<string, OrganizationScenarioTerms> Terms =
        new Dictionary<string, OrganizationScenarioTerms>(StringComparer.OrdinalIgnoreCase)
        {
            ["service-station"] = DefaultTerms with
            {
                VehiclesTitle = "Авто в работе",
                VehicleSingular = "Автомобиль",
                MaintenanceTitle = "Заказы и обслуживания",
                MaintenanceSingular = "Заказ",
                DefectsTitle = "Неисправности",
                DefectSingular = "Неисправность",
                SchedulerTitle = "План работ",
                WorkJournalTitle = "Журнал работ",
                CreateMaintenanceText = "Создать заказ",
                AddVehicleText = "Добавить авто",
                EmptyVehiclesTitle = "Авто пока не добавлены",
                EmptyVehiclesText = "Добавьте первое авто клиента, чтобы вести работы, дефекты и историю обращений."
            },
            ["detailing"] = DefaultTerms with
            {
                WorkspaceTitle = "Рабочий день",
                VehiclesTitle = "Автомобили",
                VehicleSingular = "Авто",
                MaintenanceTitle = "Услуги и записи",
                MaintenanceSingular = "Услуга",
                DefectsTitle = "Осмотр и фото",
                DefectSingular = "Фото осмотра",
                SchedulerTitle = "Записи",
                WorkJournalTitle = "Журнал услуг",
                CreateMaintenanceText = "Записать услугу",
                CreateDefectText = "Добавить фото осмотра",
                AddVehicleText = "Добавить авто",
                EmptyVehiclesTitle = "Авто клиентов пока не добавлены",
                EmptyVehiclesText = "Добавьте авто клиента, чтобы вести записи, услуги, фото и историю визитов."
            },
            ["wrapping"] = DefaultTerms with
            {
                VehiclesTitle = "Проекты и авто",
                VehicleSingular = "Авто",
                ClientsTitle = "Заказчики",
                ClientSingular = "Заказчик",
                MaintenanceTitle = "Проекты",
                MaintenanceSingular = "Проект",
                DefectsTitle = "Замечания",
                DefectSingular = "Замечание",
                SchedulerTitle = "Этапы и записи",
                WorkJournalTitle = "Журнал проектов",
                CreateMaintenanceText = "Создать проект",
                CreateDefectText = "Добавить замечание",
                AddVehicleText = "Добавить авто",
                EmptyVehiclesTitle = "Проекты пока не добавлены",
                EmptyVehiclesText = "Добавьте авто заказчика, чтобы вести этапы, материалы, фото и выдачу."
            },
            // HIDDEN_FLEET: сохранено для прежних организаций и согласованного возврата. См. docs/hidden-fleet-scenario.md.
            ["fleet"] = DefaultTerms with
            {
                VehiclesTitle = "Автопарк",
                VehicleSingular = "Единица техники",
                ClientsTitle = "Подразделения",
                ClientSingular = "Подразделение",
                MaintenanceTitle = "ТО и ремонты",
                MaintenanceSingular = "ТО/ремонт",
                DefectsTitle = "Заявки на ремонт",
                DefectSingular = "Заявка",
                CountersTitle = "Пробег и моточасы",
                SchedulerTitle = "План ТО",
                WorkJournalTitle = "Журнал эксплуатации",
                CreateMaintenanceText = "Создать ТО",
                CreateDefectText = "Создать заявку",
                EmptyVehiclesTitle = "Автопарк пока пуст",
                EmptyVehiclesText = "Добавьте первую единицу техники, чтобы контролировать ТО, пробег и ремонтные заявки."
            },
            ["driving-school"] = DefaultTerms with
            {
                VehiclesTitle = "Учебная техника",
                VehicleSingular = "Учебное авто",
                ClientsTitle = "Группы и ответственные",
                ClientSingular = "Ответственный",
                MaintenanceTitle = "Регламентные работы",
                MaintenanceSingular = "Регламентная работа",
                DefectsTitle = "Замечания по технике",
                DefectSingular = "Замечание",
                CountersTitle = "Пробег учебной техники",
                SchedulerTitle = "График готовности",
                WorkJournalTitle = "Журнал готовности",
                CreateMaintenanceText = "Создать работу",
                CreateDefectText = "Добавить замечание",
                EmptyVehiclesTitle = "Учебная техника пока не добавлена",
                EmptyVehiclesText = "Добавьте учебную машину, чтобы вести готовность, регламенты и пробег."
            }
        };

    /// <summary> Процесс сценария по умолчанию. </summary>
    private static readonly OrganizationScenarioProcess DefaultProcess = new(
        "Рабочий процесс",
        ["Запланировано", "В работе", "Выполнено", "Закрыто"],
        ["Акт работ", "История обслуживания", "Отчет по технике"],
        ["План по дате", "План по пробегу или наработке", "Контроль свежести счетчиков"],
        "Новая",
        "В работе",
        "Отложено",
        "Закрыто",
        "Взять в работу",
        "Закрыть задачу",
        "Отложить",
        "Неисправности",
        "По дате",
        "По пробегу или наработке",
        "Обновить запись",
        "Первый запуск");

    /// <summary> Профиль интерфейса по умолчанию. </summary>
    private static readonly OrganizationScenarioUxProfile DefaultUxProfile = new(
        ComplexityCode: "service",
        PreferQuickVisitForm: false,
        ShowMileageByDefault: true,
        ShowMaterialsByDefault: true,
        ShowDefectsByDefault: true,
        ShowPhotosByDefault: true,
        CreateTitle: "Добавить обслуживание",
        EditTitle: "Редактировать обслуживание",
        SubtitlePrefix: "Заполните информацию об обслуживании или ремонте ",
        LoadingText: "Загружаем форму обслуживания...",
        SaveButtonText: "Сохранить обслуживание",
        StatusLabel: "Состояние",
        TypeLabel: "Тип обслуживания",
        PlannedDateLabel: "Плановая дата",
        PlannedStartTimeLabel: "Время начала",
        PlannedEndTimeLabel: "Время окончания",
        CompletedDateLabel: "Фактическая дата выполнения",
        MileageLabel: "Пробег / наработка",
        LocationLabel: "Место проведения",
        LocationPlaceholder: "Площадка, сервис, подрядчик",
        PerformerLabel: "Исполнитель",
        PerformerPlaceholder: "Механик, бригада, подрядчик",
        PerformerHelpText: "Кто выполняет или будет выполнять это обслуживание.",
        WorksTitle: "Работы и материалы",
        DetailNameLabel: "Название",
        DetailNamePlaceholder: "Например: Замена масла",
        DetailDescriptionPlaceholder: "Что сделали, какие запчасти использовали, на что обратить внимание",
        AddDetailText: "Добавить позицию",
        CommentPlaceholder: "Что важно запомнить по этой записи");

    /// <summary> Профили интерфейса по кодам сценариев. </summary>
    private static readonly IReadOnlyDictionary<string, OrganizationScenarioUxProfile> UxProfiles =
        new Dictionary<string, OrganizationScenarioUxProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["detailing"] = DefaultUxProfile with
            {
                ComplexityCode = "quick-service",
                PreferQuickVisitForm = true,
                ShowMileageByDefault = false,
                ShowDefectsByDefault = true,
                CreateTitle = "Записать услугу",
                EditTitle = "Редактировать услугу",
                SubtitlePrefix = "Заполните запись, услугу и результат по авто ",
                LoadingText = "Загружаем форму услуги...",
                SaveButtonText = "Сохранить услугу",
                TypeLabel = "Услуга",
                PlannedDateLabel = "Дата записи",
                PlannedStartTimeLabel = "Начало записи",
                PlannedEndTimeLabel = "Окончание записи",
                CompletedDateLabel = "Дата выполнения",
                LocationLabel = "Бокс / место",
                LocationPlaceholder = "Бокс, студия, адрес клиента",
                PerformerLabel = "Мастер",
                PerformerPlaceholder = "Мастер, команда, подрядчик",
                PerformerHelpText = "Кто выполняет или будет выполнять эту услугу.",
                WorksTitle = "Услуги и материалы",
                DetailNameLabel = "Услуга",
                DetailNamePlaceholder = "Например: Комплексная мойка, химчистка салона или полировка кузова"
            },
            ["wrapping"] = DefaultUxProfile with
            {
                ComplexityCode = "project",
                ShowMileageByDefault = false,
                CreateTitle = "Создать проект",
                EditTitle = "Редактировать проект",
                SaveButtonText = "Сохранить проект",
                TypeLabel = "Этап / тип проекта",
                PlannedDateLabel = "Дата этапа",
                PlannedStartTimeLabel = "Начало этапа",
                PlannedEndTimeLabel = "Окончание этапа",
                WorksTitle = "Работы и материалы проекта",
                DetailNamePlaceholder = "Например: Оклейка капота"
            },
            ["fleet"] = DefaultUxProfile with
            {
                ComplexityCode = "fleet",
                ShowPhotosByDefault = false,
                CreateTitle = "Создать ТО",
                SaveButtonText = "Сохранить ТО",
                TypeLabel = "Тип ТО / ремонта",
                PlannedDateLabel = "Дата ТО",
                LocationPlaceholder = "Парк, ремзона, подрядчик",
                PerformerPlaceholder = "Механик, смена, подрядчик"
            },
            ["driving-school"] = DefaultUxProfile with
            {
                ComplexityCode = "fleet",
                ShowMaterialsByDefault = false,
                ShowPhotosByDefault = false,
                CreateTitle = "Создать работу",
                SaveButtonText = "Сохранить работу",
                TypeLabel = "Регламент / работа",
                PlannedDateLabel = "Дата готовности",
                LocationPlaceholder = "Парк, класс, сервис",
                PerformerPlaceholder = "Ответственный, механик, инструктор"
            },
        };

    /// <summary> Процессы по кодам сценариев. </summary>
    private static readonly IReadOnlyDictionary<string, OrganizationScenarioProcess> Processes =
        new Dictionary<string, OrganizationScenarioProcess>(StringComparer.OrdinalIgnoreCase)
        {
            ["service-station"] = DefaultProcess with
            {
                WorkPipelineTitle = "Процесс СТО",
                WorkStages = ["Запись", "Приемка", "Диагностика", "В работе", "Готово к выдаче", "Выдано"],
                DocumentTemplates = ["Заказ-наряд", "Акт выполненных работ", "Рекомендации клиенту"],
                PlanningRules = ["Повторный визит по дате", "ТО по пробегу", "Открытые неисправности сразу попадают в план"],
                NewWorkflowTitle = "Новая запись",
                InProgressWorkflowTitle = "В работе",
                DeferredWorkflowTitle = "Ожидает",
                ClosedWorkflowTitle = "Закрыто",
                DefectSourceTitle = "Неисправности",
                DateSourceTitle = "Плановый визит",
                MileageSourceTitle = "ТО по пробегу",
                CounterSourceTitle = "Обновить пробег",
                SetupSourceTitle = "Первичная приемка"
            },
            ["detailing"] = DefaultProcess with
            {
                WorkPipelineTitle = "Процесс детейлинга",
                WorkStages = ["Запись", "Осмотр", "Подготовка", "Работа", "Контроль качества", "Выдача"],
                DocumentTemplates = ["Акт приема", "Фото до/после", "Акт выдачи"],
                PlanningRules = ["Запись по дате и времени", "Контроль этапов работ", "Фото осмотра и результата"],
                NewWorkflowTitle = "Новая запись",
                InProgressWorkflowTitle = "В работе",
                DeferredWorkflowTitle = "Ожидает",
                ClosedWorkflowTitle = "Выдано",
                CloseActionText = "Завершить",
                DefectSourceTitle = "Фото осмотра",
                DateSourceTitle = "Запись по дате",
                MileageSourceTitle = "Повторный визит",
                CounterSourceTitle = "Обновить данные",
                SetupSourceTitle = "Первичный осмотр"
            },
            ["wrapping"] = DefaultProcess with
            {
                WorkPipelineTitle = "Процесс оклейки",
                WorkStages = ["Замер", "Подготовка", "Оклейка", "Выдержка", "Контроль", "Выдача"],
                DocumentTemplates = ["Заказ проекта", "Спецификация материалов", "Акт выдачи"],
                PlanningRules = ["Этапы проекта по датам", "Контроль выдержки", "Замечания после контроля остаются в плане"],
                NewWorkflowTitle = "Новый проект",
                InProgressWorkflowTitle = "В процессе",
                DeferredWorkflowTitle = "Ожидает",
                ClosedWorkflowTitle = "Выдано",
                StartActionText = "Запустить этап",
                CloseActionText = "Закрыть этап",
                DefectSourceTitle = "Замечания",
                DateSourceTitle = "Этап по дате",
                MileageSourceTitle = "Повторный визит",
                CounterSourceTitle = "Обновить данные",
                SetupSourceTitle = "Первичный замер"
            },
            ["fleet"] = DefaultProcess with
            {
                WorkPipelineTitle = "Процесс автопарка",
                WorkStages = ["Осмотр", "План ТО", "В ремонте", "Проверка", "Готова к линии"],
                DocumentTemplates = ["Заявка на ремонт", "Акт осмотра", "Отчет по ТО"],
                PlanningRules = ["ТО по пробегу/моточасам", "Просроченные заявки выше в списке", "Счетчики требуют регулярного обновления"],
                NewWorkflowTitle = "Новая заявка",
                InProgressWorkflowTitle = "В ремонте",
                DeferredWorkflowTitle = "Ожидает",
                ClosedWorkflowTitle = "Готово",
                StartActionText = "Взять в ремонт",
                CloseActionText = "Готово",
                DefectSourceTitle = "Заявки на ремонт",
                DateSourceTitle = "Плановое ТО",
                MileageSourceTitle = "ТО по ресурсу",
                CounterSourceTitle = "Обновить счетчики",
                SetupSourceTitle = "Первичный осмотр"
            },
            ["driving-school"] = DefaultProcess with
            {
                WorkPipelineTitle = "Процесс автошколы",
                WorkStages = ["Осмотр", "Плановая работа", "На обслуживании", "Проверка", "Готова к занятиям"],
                DocumentTemplates = ["Акт осмотра", "Регламентная работа", "Отчет готовности"],
                PlanningRules = ["Регламент по дате", "Пробег учебной техники", "Замечания выводятся в график готовности"],
                NewWorkflowTitle = "Новая",
                InProgressWorkflowTitle = "На обслуживании",
                DeferredWorkflowTitle = "Ожидает",
                ClosedWorkflowTitle = "Готово",
                StartActionText = "Начать работу",
                CloseActionText = "Готово",
                DefectSourceTitle = "Замечания",
                DateSourceTitle = "Регламент по дате",
                MileageSourceTitle = "Регламент по пробегу",
                CounterSourceTitle = "Обновить пробег",
                SetupSourceTitle = "Первичная проверка"
            }
        };

    /// <summary> Готовые презентации сценариев по кодам. </summary>
    private static readonly IReadOnlyDictionary<string, OrganizationScenarioPresentation> Presets =
        new Dictionary<string, OrganizationScenarioPresentation>(StringComparer.OrdinalIgnoreCase)
        {
            ["service-station"] = Preset(
                "service-station",
                "СТО и сервис",
                "Клиенты, техника, обслуживание, неисправности и план работ.",
                ["Клиенты", "Техника", "Обслуживания", "Неисправности", "Планировщик"],
                ["Быстрый прием техники", "История работ по клиенту", "Контроль открытых проблем"],
                ["Вести клиентов и автомобили", "Создавать заказы и обслуживания", "Формировать заказ-наряды и акты", "Планировать работы мастеров", "Учитывать материалы и запчасти"],
                string.Empty,
                [
                    Metric("clients.total", "Клиенты", "#clients", FeatureCodes.ClientsModule),
                    Metric("maintenance.inProgress", "В работе", "#maintenance", FeatureCodes.VehiclesModule),
                    Metric("defects.open", "Открытые неисправности", "#defects", FeatureCodes.DefectsModule),
                    Metric("planner.overdue", "Просрочено", "#scheduler?quick=overdue", FeatureCodes.SchedulerModule)
                ],
                [
                    Action("Клиенты", "#clients", "fa fa-users", FeatureCodes.ClientsModule, true),
                    Action("Создать технику", "#vehicles/new", "fa fa-car", FeatureCodes.VehiclesModule),
                    Action("Планирование", "#scheduler", "fa fa-calendar", FeatureCodes.SchedulerModule),
                    Action("Журнал работ", "#work-journal", "fa fa-list-alt", null)
                ]),
            ["detailing"] = Preset(
                "detailing",
                "Детейлинг",
                "Клиентские авто, записи на детейлинг, химчистку, мойку, фото и история услуг.",
                ["Клиенты", "Авто клиентов", "Записи", "Фото работ", "Планировщик"],
                ["Быстрая запись", "Осмотр авто", "Фото и история услуг"],
                ["Вести базу клиентов", "Планировать записи по времени", "Хранить историю услуг", "Хранить фото осмотра и результата", "Учитывать материалы детейлинга"],
                string.Empty,
                [
                    Metric("clients.total", "Клиенты", "#clients", FeatureCodes.ClientsModule),
                    Metric("maintenance.planned", "Запланировано", "#scheduler", FeatureCodes.SchedulerModule),
                    Metric("maintenance.inProgress", "В работе", "#maintenance", FeatureCodes.VehiclesModule),
                    Metric("documents.total", "Документы", "#work-journal", null)
                ],
                [
                    Action("Клиенты", "#clients", "fa fa-users", FeatureCodes.ClientsModule, true),
                    Action("Записать услугу", "#scheduler", "fa fa-calendar", FeatureCodes.SchedulerModule),
                    Action("Создать технику", "#vehicles/new", "fa fa-car", FeatureCodes.VehiclesModule),
                    Action("Журнал работ", "#work-journal", "fa fa-list-alt", null)
                ]),
            ["wrapping"] = Preset(
                "wrapping",
                "Оклейка и тюнинг",
                "Заказы по клиентской технике, этапы работ и история обращений.",
                ["Клиенты", "Техника", "Обслуживания", "Планировщик"],
                ["Этапы заказа", "Материалы", "Коммуникация с клиентом"],
                ["Вести проекты оклейки", "Разбивать работу на этапы", "Планировать выдачу", "Учитывать пленку и материалы", "Формировать документы по проекту"],
                string.Empty,
                [
                    Metric("maintenance.planned", "Запланировано", "#scheduler", FeatureCodes.SchedulerModule),
                    Metric("maintenance.inProgress", "В работе", "#maintenance", FeatureCodes.VehiclesModule),
                    Metric("clients.total", "Клиенты", "#clients", FeatureCodes.ClientsModule),
                    Metric("documents.total", "Документы", "#work-journal", null)
                ],
                [
                    Action("Планирование", "#scheduler", "fa fa-calendar", FeatureCodes.SchedulerModule, true),
                    Action("Клиенты", "#clients", "fa fa-users", FeatureCodes.ClientsModule),
                    Action("Создать технику", "#vehicles/new", "fa fa-car", FeatureCodes.VehiclesModule),
                    Action("Журнал работ", "#work-journal", "fa fa-list-alt", null)
                ]),
            ["fleet"] = Preset(
                "fleet",
                "Автопарк",
                "Внутренняя техника, пробег, ТО, неисправности и контроль готовности.",
                ["Техника", "Пробег и наработка", "Обслуживания", "Неисправности", "Планировщик"],
                ["Плановое ТО", "Пробег/моточасы", "Приоритеты ремонта"],
                ["Контролировать парк техники", "Вести пробег и моточасы", "Планировать ТО", "Фиксировать ремонтные заявки", "Смотреть готовность техники к линии"],
                string.Empty,
                [
                    Metric("vehicles.active", "Активная техника", "#vehicles", FeatureCodes.VehiclesModule),
                    Metric("planner.overdue", "Просрочено", "#scheduler?quick=overdue", FeatureCodes.SchedulerModule),
                    Metric("defects.open", "Открытые неисправности", "#defects", FeatureCodes.DefectsModule),
                    Metric("counters.entries", "Записи пробега", "#counters", FeatureCodes.CountersModule)
                ],
                [
                    Action("Техника", "#vehicles", "fa fa-car", FeatureCodes.VehiclesModule, true),
                    Action("Пробег", "#counters", "fa fa-tachometer", FeatureCodes.CountersModule),
                    Action("Неисправности", "#defects", "fa fa-exclamation-triangle", FeatureCodes.DefectsModule),
                    Action("Планирование", "#scheduler", "fa fa-calendar", FeatureCodes.SchedulerModule)
                ]),
            ["driving-school"] = Preset(
                "driving-school",
                "Автошкола",
                "Учебная техника, регламентные работы, пробег и доступ сотрудников.",
                ["Техника", "Пробег и наработка", "Обслуживания", "Планировщик"],
                ["Готовность машин", "Регламент обслуживания", "Ответственные сотрудники"],
                ["Вести учебные автомобили", "Контролировать готовность к занятиям", "Планировать регламентные работы", "Назначать ответственных", "Хранить документы по технике"],
                string.Empty,
                [
                    Metric("vehicles.active", "Учебная техника", "#vehicles", FeatureCodes.VehiclesModule),
                    Metric("maintenance.planned", "Плановые работы", "#scheduler", FeatureCodes.SchedulerModule),
                    Metric("planner.soon", "Скоро", "#scheduler?quick=soon", FeatureCodes.SchedulerModule),
                    Metric("counters.entries", "Пробег", "#counters", FeatureCodes.CountersModule)
                ],
                [
                    Action("Техника", "#vehicles", "fa fa-car", FeatureCodes.VehiclesModule, true),
                    Action("Планирование", "#scheduler", "fa fa-calendar", FeatureCodes.SchedulerModule),
                    Action("Пробег", "#counters", "fa fa-tachometer", FeatureCodes.CountersModule),
                    Action("Участники", "#members", "fa fa-users", null)
                ]),
            ["personal"] = Preset(
                "personal",
                "Личный учет",
                "Минимальный набор для своей техники без клиентской структуры.",
                ["Техника", "Обслуживания", "Пробег и наработка"],
                ["История владения", "Плановое обслуживание", "Документы"],
                ["Вести личную технику", "Хранить историю обслуживания", "Следить за пробегом", "Готовить PDF/Word отчеты", "Сохранять документы и фото"],
                "Личный сценарий держит фокус на истории машины, документах и напоминаниях.",
                [
                    Metric("vehicles.active", "Техника", "#vehicles", FeatureCodes.VehiclesModule),
                    Metric("maintenance.total", "Обслуживания", "#maintenance", FeatureCodes.VehiclesModule),
                    Metric("counters.entries", "Пробег и наработка", "#counters", FeatureCodes.CountersModule),
                    Metric("documents.total", "Документы", "#work-journal", null)
                ],
                [
                    Action("Техника", "#vehicles", "fa fa-car", FeatureCodes.VehiclesModule, true),
                    Action("Создать технику", "#vehicles/new", "fa fa-plus", FeatureCodes.VehiclesModule),
                    Action("Пробег", "#counters", "fa fa-tachometer", FeatureCodes.CountersModule),
                    Action("Журнал", "#work-journal", "fa fa-list-alt", null)
                ])
        };

    /// <summary>
    /// Возвращает UI-пресет типа организации или fallback-пресет для неизвестного сценария.
    /// </summary>
    public static OrganizationScenarioPresentation Resolve(string? code, string? fallbackTitle = null)
    {
        var normalizedCode = NormalizeScenarioCode(code);

        return Presets.TryGetValue(normalizedCode, out var preset)
            ? preset
            : Preset(
                normalizedCode,
                string.IsNullOrWhiteSpace(fallbackTitle) ? "Организация" : fallbackTitle.Trim(),
                "Базовый набор возможностей для рабочего пространства.",
                [],
                [],
                ["Вести технику", "Создавать обслуживания", "Работать с клиентами", "Формировать документы"],
                "Сценарий пока не имеет отдельного dashboard-пресета.",
                [
                    Metric("vehicles.active", "Активная техника", "#vehicles", FeatureCodes.VehiclesModule),
                    Metric("maintenance.total", "Обслуживания", "#maintenance", FeatureCodes.VehiclesModule),
                    Metric("clients.total", "Клиенты", "#clients", FeatureCodes.ClientsModule),
                    Metric("defects.open", "Открытые неисправности", "#defects", FeatureCodes.DefectsModule)
                ],
                [
                    Action("Техника", "#vehicles", "fa fa-car", FeatureCodes.VehiclesModule, true),
                    Action("Клиенты", "#clients", "fa fa-users", FeatureCodes.ClientsModule),
                    Action("Планирование", "#scheduler", "fa fa-calendar", FeatureCodes.SchedulerModule),
                    Action("Журнал работ", "#work-journal", "fa fa-list-alt", null)
                ]);
    }

    /// <summary>
    /// Возвращает русскую терминологию разделов и действий для выбранного типа организации.
    /// </summary>
    public static OrganizationScenarioTerms ResolveTerms(string? code)
    {
        var normalizedCode = NormalizeScenarioCode(code);

        return Terms.TryGetValue(normalizedCode, out var terms)
            ? terms
            : DefaultTerms;
    }

    /// <summary>
    /// Возвращает UX-профиль формы обслуживания для выбранного типа организации.
    /// </summary>
    public static OrganizationScenarioUxProfile ResolveUxProfile(string? code)
    {
        var normalizedCode = NormalizeScenarioCode(code);

        return UxProfiles.TryGetValue(normalizedCode, out var profile)
            ? profile
            : DefaultUxProfile;
    }

    /// <summary>
    /// Возвращает presentation-модель процесса, построенную из общего backend-каталога сценариев.
    /// </summary>
    public static OrganizationScenarioProcess ResolveProcess(string? code)
    {
        var process = OrganizationScenarioProcessCatalog.Resolve(NormalizeScenarioCode(code));
        return new OrganizationScenarioProcess(
            process.WorkPipelineTitle,
            process.WorkStages.Select(x => x.Name).ToList(),
            process.DocumentTemplates.Select(x => x.Name).ToList(),
            process.PlanningRules,
            process.NewWorkflowTitle,
            process.InProgressWorkflowTitle,
            process.DeferredWorkflowTitle,
            process.ClosedWorkflowTitle,
            process.StartActionText,
            process.CloseActionText,
            process.DeferActionText,
            process.DefectSourceTitle,
            process.DateSourceTitle,
            process.MileageSourceTitle,
            process.CounterSourceTitle,
            process.SetupSourceTitle);
    }

    /// <summary> Приводит код сценария к нормализованному виду. </summary>
    private static string NormalizeScenarioCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return OrganizationTypeCodes.ServiceStation;
        }

        return code.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Преобразует включенные feature flags рабочего пространства в русские названия модулей.
    /// </summary>
    public static IReadOnlyList<string> ResolveEnabledModuleNames(IEnumerable<FeatureValueDto> features)
    {
        return features
            .Where(x => x.BoolValue == true)
            .Select(x => x.Code)
            .Select(GetModuleName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Проверяет, можно ли показывать сценарный блок с учетом включенных фич тарифа.
    /// </summary>
    public static bool IsFeatureEnabled(IEnumerable<FeatureValueDto> features, string? featureCode)
    {
        return string.IsNullOrWhiteSpace(featureCode)
            || features.Any(x => string.Equals(x.Code, featureCode, StringComparison.OrdinalIgnoreCase) && x.BoolValue == true);
    }

    /// <summary>
    /// Подставляет идентификатор организации в url-template сценарного действия или метрики.
    /// </summary>
    public static string ResolveUrl(string urlTemplate, Guid organizationId)
    {
        return urlTemplate.Replace("{organizationId}", organizationId.ToString("D"), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary> Возвращает название модуля по коду функции. </summary>
    private static string GetModuleName(string? featureCode) => featureCode switch
    {
        FeatureCodes.VehiclesModule => "Техника",
        FeatureCodes.ClientsModule => "Клиенты",
        FeatureCodes.DefectsModule => "Неисправности",
        FeatureCodes.CountersModule => "Пробег и наработка",
        FeatureCodes.MaintenanceModule => "Обслуживание",
        FeatureCodes.SchedulerModule => "Планировщик",
        FeatureCodes.RemindersModule => "Напоминания",
        FeatureCodes.DocumentsModule => "Документы",
        FeatureCodes.MaterialsModule => "Материалы",
        FeatureCodes.WorkJournalModule => "Журнал работ",
        FeatureCodes.FinanceModule => "Финансы",
        FeatureCodes.AnalyticsModule => "Статистика",
        FeatureCodes.WikiModule => "Вики организации",
        FeatureCodes.MembersModule => "Участники",
        FeatureCodes.BillingModule => "Тарифы и оплата",
        FeatureCodes.SettingsModule => "Настройки организации",
        _ => string.Empty
    };

    /// <summary> Создаёт презентацию сценария с заданными параметрами. </summary>
    private static OrganizationScenarioPresentation Preset(
        string code,
        string title,
        string summary,
        IReadOnlyList<string> modules,
        IReadOnlyList<string> focus,
        IReadOnlyList<string> capabilities,
        string dashboardHint,
        IReadOnlyList<OrganizationScenarioMetric> metrics,
        IReadOnlyList<OrganizationScenarioAction> actions) =>
        new(code, title, summary, modules, focus, capabilities, dashboardHint, metrics, actions);

    /// <summary> Создаёт описание метрики сценария. </summary>
    private static OrganizationScenarioMetric Metric(
        string code,
        string title,
        string urlTemplate,
        string? requiredFeatureCode) =>
        new(code, title, urlTemplate, requiredFeatureCode);

    /// <summary> Создаёт описание действия сценария. </summary>
    private static OrganizationScenarioAction Action(
        string title,
        string urlTemplate,
        string iconCssClass,
        string? requiredFeatureCode,
        bool isPrimary = false) =>
        new(title, urlTemplate, iconCssClass, requiredFeatureCode, isPrimary);
}
