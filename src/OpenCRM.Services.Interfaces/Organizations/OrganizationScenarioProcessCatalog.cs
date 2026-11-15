using OpenCRM.Domain;

namespace OpenCRM.Services.Interfaces.Organizations;

/// <summary>
/// Этап рабочего процесса организации под конкретный сценарий.
/// </summary>
public sealed record OrganizationScenarioStageDefinition(string Code, string Name);

/// <summary>
/// Документ сценария: пользовательское название и стабильный тип генератора.
/// </summary>
public sealed record OrganizationScenarioDocumentDefinition(string Name, string KindCode);

/// <summary>
/// Backend-описание процесса организации: этапы, документы, правила планирования и подписи workflow.
/// </summary>
public sealed record OrganizationScenarioProcessDefinition(
    string Code,
    string WorkPipelineTitle,
    IReadOnlyList<OrganizationScenarioStageDefinition> WorkStages,
    IReadOnlyList<OrganizationScenarioDocumentDefinition> DocumentTemplates,
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
/// Стабильные коды этапов сценарных workflow.
/// </summary>
public static class OrganizationScenarioWorkflowStageCodes
{
    /// <summary>Прием, запись или первичная фиксация задачи.</summary>
    public const string Intake = "intake";

    /// <summary>Осмотр техники или объекта.</summary>
    public const string Inspection = "inspection";

    /// <summary>Диагностика перед работой.</summary>
    public const string Diagnostics = "diagnostics";

    /// <summary>Подготовительный этап.</summary>
    public const string Preparation = "preparation";

    /// <summary>Основная работа.</summary>
    public const string Work = "work";

    /// <summary>Ожидание внешнего действия, клиента, материала или времени.</summary>
    public const string Waiting = "waiting";

    /// <summary>Контроль качества или финальная проверка.</summary>
    public const string QualityControl = "quality_control";

    /// <summary>Готовность к выдаче или возврату в эксплуатацию.</summary>
    public const string Ready = "ready";

    /// <summary>Завершенный этап.</summary>
    public const string Completed = "completed";
}

/// <summary>
/// Общий каталог сценарных процессов, используемый BL, MVC, API и будущими клиентами.
/// </summary>
public static class OrganizationScenarioProcessCatalog
{
    /// <summary> Процесс сценария по умолчанию. </summary>
    private static readonly OrganizationScenarioProcessDefinition DefaultProcess = Process(
        OrganizationTypeCodes.Personal,
        "Рабочий процесс",
        [
            Stage(OrganizationScenarioWorkflowStageCodes.Intake, "Запланировано"),
            Stage(OrganizationScenarioWorkflowStageCodes.Work, "В работе"),
            Stage(OrganizationScenarioWorkflowStageCodes.Completed, "Выполнено")
        ],
        [
            Document("Акт работ", "work-act"),
            Document("История обслуживания", "maintenance-report"),
            Document("Отчет по технике", "maintenance-report")
        ],
        ["План по дате", "План по пробегу или наработке", "Контроль свежести счетчиков", "Первичный запуск"],
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

    /// <summary> Справочник процессов по кодам сценариев. </summary>
    private static readonly IReadOnlyDictionary<string, OrganizationScenarioProcessDefinition> Processes =
        new Dictionary<string, OrganizationScenarioProcessDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            [OrganizationTypeCodes.ServiceStation] = Process(
                OrganizationTypeCodes.ServiceStation,
                "Процесс СТО",
                [
                    Stage(OrganizationScenarioWorkflowStageCodes.Intake, "Запись"),
                    Stage(OrganizationScenarioWorkflowStageCodes.Inspection, "Приемка"),
                    Stage(OrganizationScenarioWorkflowStageCodes.Diagnostics, "Диагностика"),
                    Stage(OrganizationScenarioWorkflowStageCodes.Work, "В работе"),
                    Stage(OrganizationScenarioWorkflowStageCodes.Ready, "Готово к выдаче"),
                    Stage(OrganizationScenarioWorkflowStageCodes.Completed, "Выдано")
                ],
                [
                    Document("Заказ-наряд", "work-order"),
                    Document("Акт выполненных работ", "work-act"),
                    Document("Рекомендации клиенту", "recommendations"),
                    Document("Лист диагностики", "inspection-act")
                ],
                ["Повторный визит по дате", "ТО по пробегу", "Открытые неисправности сразу попадают в план", "Первичная приемка", "Контроль свежести пробега"],
                "Новая запись",
                "В работе",
                "Ожидает",
                "Закрыто",
                "Взять в работу",
                "Закрыть задачу",
                "Отложить",
                "Неисправности",
                "Плановый визит",
                "ТО по пробегу",
                "Обновить пробег",
                "Первичная приемка"),
            [OrganizationTypeCodes.Detailing] = Process(
                OrganizationTypeCodes.Detailing,
                "Процесс детейлинга",
                [
                    Stage(OrganizationScenarioWorkflowStageCodes.Intake, "Запись"),
                    Stage(OrganizationScenarioWorkflowStageCodes.Inspection, "Осмотр"),
                    Stage(OrganizationScenarioWorkflowStageCodes.Preparation, "Подготовка"),
                    Stage(OrganizationScenarioWorkflowStageCodes.Work, "Работа"),
                    Stage(OrganizationScenarioWorkflowStageCodes.QualityControl, "Контроль качества"),
                    Stage(OrganizationScenarioWorkflowStageCodes.Completed, "Выдача")
                ],
                [
                    Document("Акт приема автомобиля", "inspection-act"),
                    Document("Отчет по проекту с фото", "photo-report"),
                    Document("Акт выдачи", "work-act"),
                    Document("Рекомендации по уходу", "recommendations")
                ],
                ["Запись по дате и времени", "Контроль этапов работ", "Фото осмотра и результата", "Первичный осмотр"],
                "Новая запись",
                "В работе",
                "Ожидает",
                "Выдано",
                "Взять в работу",
                "Завершить",
                "Отложить",
                "Фото осмотра",
                "Запись по дате",
                "Повторный визит",
                "Обновить данные",
                "Первичный осмотр"),
            [OrganizationTypeCodes.Wrapping] = Process(
                OrganizationTypeCodes.Wrapping,
                "Процесс оклейки",
                [
                    Stage(OrganizationScenarioWorkflowStageCodes.Intake, "Замер"),
                    Stage(OrganizationScenarioWorkflowStageCodes.Preparation, "Подготовка"),
                    Stage(OrganizationScenarioWorkflowStageCodes.Work, "Оклейка"),
                    Stage(OrganizationScenarioWorkflowStageCodes.Waiting, "Выдержка"),
                    Stage(OrganizationScenarioWorkflowStageCodes.QualityControl, "Контроль"),
                    Stage(OrganizationScenarioWorkflowStageCodes.Completed, "Выдача")
                ],
                [
                    Document("Заказ проекта", "work-order"),
                    Document("Спецификация материалов", "materials-spec"),
                    Document("Акт приема автомобиля", "inspection-act"),
                    Document("Акт выдачи", "work-act"),
                    Document("Памятка после оклейки", "recommendations")
                ],
                ["Этапы проекта по датам", "Контроль выдержки", "Замечания после контроля остаются в плане", "Первичный замер"],
                "Новый проект",
                "В процессе",
                "Ожидает",
                "Выдано",
                "Запустить этап",
                "Закрыть этап",
                "Отложить",
                "Замечания",
                "Этап по дате",
                "Повторный визит",
                "Обновить данные",
                "Первичный замер"),
            [OrganizationTypeCodes.CarWash] = Process(
                OrganizationTypeCodes.CarWash,
                "Процесс автомойки",
                [
                    Stage(OrganizationScenarioWorkflowStageCodes.Intake, "Запись"),
                    Stage(OrganizationScenarioWorkflowStageCodes.Work, "Мойка"),
                    Stage(OrganizationScenarioWorkflowStageCodes.Completed, "Готово")
                ],
                [
                    Document("Чек услуги", "receipt"),
                    Document("Акт оказанных услуг", "work-act"),
                    Document("Отчет по визиту", "maintenance-report")
                ],
                ["Запись по дате и времени", "Очередь на сегодня", "Повторный визит"],
                "Новая запись",
                "Моется",
                "Ожидает",
                "Готово",
                "Начать мойку",
                "Готово",
                "Отложить",
                "Замечания",
                "Запись по времени",
                "Повторный визит",
                "Обновить данные",
                "Первичный визит"),
            // HIDDEN_FLEET: сохранено для прежних организаций и согласованного возврата. См. docs/hidden-fleet-scenario.md.
            [OrganizationTypeCodes.Fleet] = Process(
                OrganizationTypeCodes.Fleet,
                "Процесс автопарка",
                [
                    Stage(OrganizationScenarioWorkflowStageCodes.Inspection, "Осмотр"),
                    Stage(OrganizationScenarioWorkflowStageCodes.Intake, "План ТО"),
                    Stage(OrganizationScenarioWorkflowStageCodes.Work, "В ремонте"),
                    Stage(OrganizationScenarioWorkflowStageCodes.QualityControl, "Проверка"),
                    Stage(OrganizationScenarioWorkflowStageCodes.Completed, "Готова к линии")
                ],
                [
                    Document("Заявка на ремонт", "work-order"),
                    Document("Акт осмотра", "inspection-act"),
                    Document("Отчет по ТО", "maintenance-report"),
                    Document("Отчет затрат", "materials-spec")
                ],
                ["ТО по пробегу/моточасам", "Просроченные заявки выше в списке", "Счетчики требуют регулярного обновления", "Первичный осмотр"],
                "Новая заявка",
                "В ремонте",
                "Ожидает",
                "Готово",
                "Взять в ремонт",
                "Готово",
                "Отложить",
                "Заявки на ремонт",
                "Плановое ТО",
                "ТО по ресурсу",
                "Обновить счетчики",
                "Первичный осмотр"),
            [OrganizationTypeCodes.DrivingSchool] = Process(
                OrganizationTypeCodes.DrivingSchool,
                "Процесс автошколы",
                [
                    Stage(OrganizationScenarioWorkflowStageCodes.Inspection, "Осмотр"),
                    Stage(OrganizationScenarioWorkflowStageCodes.Intake, "Плановая работа"),
                    Stage(OrganizationScenarioWorkflowStageCodes.Work, "На обслуживании"),
                    Stage(OrganizationScenarioWorkflowStageCodes.QualityControl, "Проверка"),
                    Stage(OrganizationScenarioWorkflowStageCodes.Completed, "Готова к занятиям")
                ],
                [
                    Document("Акт осмотра", "inspection-act"),
                    Document("Регламентная работа", "work-order"),
                    Document("Отчет готовности", "readiness-report"),
                    Document("Журнал замечаний инструктора", "checklist")
                ],
                ["Регламент по дате", "Пробег учебной техники", "Замечания выводятся в график готовности", "Контроль свежести пробега", "Первичная проверка"],
                "Новая",
                "На обслуживании",
                "Ожидает",
                "Готово",
                "Начать работу",
                "Готово",
                "Отложить",
                "Замечания",
                "Регламент по дате",
                "Регламент по пробегу",
                "Обновить пробег",
                "Первичная проверка")
        };

    /// <summary> Определяет процесс по коду сценария. </summary>
    public static OrganizationScenarioProcessDefinition Resolve(string? scenarioCode)
    {
        var normalizedCode = NormalizeScenarioCode(scenarioCode);
        return Processes.TryGetValue(normalizedCode, out var process)
            ? process
            : DefaultProcess;
    }

    /// <summary> Определяет код этапа по сценарию, статусу и источнику. </summary>
    public static string ResolveStageCode(string? scenarioCode, string? workflowStatusCode, string? sourceCode)
    {
        var status = OrganizationSchedulerTaskStatusCodes.Normalize(workflowStatusCode);
        if (status == OrganizationSchedulerTaskStatusCodes.Closed)
        {
            return OrganizationScenarioWorkflowStageCodes.Completed;
        }

        if (status == OrganizationSchedulerTaskStatusCodes.Deferred)
        {
            return OrganizationScenarioWorkflowStageCodes.Waiting;
        }

        if (status == OrganizationSchedulerTaskStatusCodes.InProgress)
        {
            return OrganizationScenarioWorkflowStageCodes.Work;
        }

        var process = Resolve(scenarioCode);
        var normalizedSource = sourceCode?.Trim().ToLowerInvariant();
        return normalizedSource switch
        {
            "defect" when process.Code == OrganizationTypeCodes.ServiceStation => OrganizationScenarioWorkflowStageCodes.Diagnostics,
            "defect" when process.Code == OrganizationTypeCodes.Fleet => OrganizationScenarioWorkflowStageCodes.Inspection,
            "defect" => OrganizationScenarioWorkflowStageCodes.QualityControl,
            "setup" => process.Code switch
            {
                OrganizationTypeCodes.Detailing => OrganizationScenarioWorkflowStageCodes.Inspection,
                OrganizationTypeCodes.Wrapping => OrganizationScenarioWorkflowStageCodes.Intake,
                OrganizationTypeCodes.Fleet => OrganizationScenarioWorkflowStageCodes.Inspection,
                OrganizationTypeCodes.DrivingSchool => OrganizationScenarioWorkflowStageCodes.Inspection,
                _ => OrganizationScenarioWorkflowStageCodes.Intake
            },
            _ => OrganizationScenarioWorkflowStageCodes.Intake
        };
    }

    /// <summary> Возвращает название этапа процесса по коду. </summary>
    public static string ResolveStageName(string? scenarioCode, string? stageCode)
    {
        var process = Resolve(scenarioCode);
        var normalizedStage = NormalizeStageCode(stageCode);
        return process.WorkStages.FirstOrDefault(x => string.Equals(x.Code, normalizedStage, StringComparison.OrdinalIgnoreCase))?.Name
            ?? DefaultProcess.WorkStages.FirstOrDefault(x => string.Equals(x.Code, normalizedStage, StringComparison.OrdinalIgnoreCase))?.Name
            ?? normalizedStage;
    }

    /// <summary> Приводит код этапа к каноническому виду. </summary>
    public static string NormalizeStageCode(string? stageCode)
    {
        var normalized = stageCode?.Trim().ToLowerInvariant();
        return normalized switch
        {
            OrganizationScenarioWorkflowStageCodes.Inspection => OrganizationScenarioWorkflowStageCodes.Inspection,
            OrganizationScenarioWorkflowStageCodes.Diagnostics => OrganizationScenarioWorkflowStageCodes.Diagnostics,
            OrganizationScenarioWorkflowStageCodes.Preparation => OrganizationScenarioWorkflowStageCodes.Preparation,
            OrganizationScenarioWorkflowStageCodes.Work => OrganizationScenarioWorkflowStageCodes.Work,
            OrganizationScenarioWorkflowStageCodes.Waiting => OrganizationScenarioWorkflowStageCodes.Waiting,
            OrganizationScenarioWorkflowStageCodes.QualityControl => OrganizationScenarioWorkflowStageCodes.QualityControl,
            OrganizationScenarioWorkflowStageCodes.Ready => OrganizationScenarioWorkflowStageCodes.Ready,
            OrganizationScenarioWorkflowStageCodes.Completed => OrganizationScenarioWorkflowStageCodes.Completed,
            _ => OrganizationScenarioWorkflowStageCodes.Intake
        };
    }

    /// <summary> Приводит код сценария к каноническому виду. </summary>
    public static string NormalizeScenarioCode(string? scenarioCode)
    {
        var normalized = scenarioCode?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return OrganizationTypeCodes.ServiceStation;
        }

        return normalized switch
        {
            "service" or "service_station" or "auto-service" or "autoservice" or "car-service" or "sto"
                or "repair" or "repair-shop" or "garage" or "workshop" =>
                OrganizationTypeCodes.ServiceStation,
            "detail" => OrganizationTypeCodes.Detailing,
            "wrap" => OrganizationTypeCodes.Wrapping,
            "school" or "driving" => OrganizationTypeCodes.DrivingSchool,
            _ => normalized
        };
    }

    /// <summary> Создаёт определение процесса со стадиями, документами и подписями. </summary>
    private static OrganizationScenarioProcessDefinition Process(
        string code,
        string workPipelineTitle,
        IReadOnlyList<OrganizationScenarioStageDefinition> workStages,
        IReadOnlyList<OrganizationScenarioDocumentDefinition> documentTemplates,
        IReadOnlyList<string> planningRules,
        string newWorkflowTitle,
        string inProgressWorkflowTitle,
        string deferredWorkflowTitle,
        string closedWorkflowTitle,
        string startActionText,
        string closeActionText,
        string deferActionText,
        string defectSourceTitle,
        string dateSourceTitle,
        string mileageSourceTitle,
        string counterSourceTitle,
        string setupSourceTitle) =>
        new(
            code,
            workPipelineTitle,
            workStages,
            documentTemplates,
            planningRules,
            newWorkflowTitle,
            inProgressWorkflowTitle,
            deferredWorkflowTitle,
            closedWorkflowTitle,
            startActionText,
            closeActionText,
            deferActionText,
            defectSourceTitle,
            dateSourceTitle,
            mileageSourceTitle,
            counterSourceTitle,
            setupSourceTitle);

    /// <summary> Создаёт определение этапа процесса. </summary>
    private static OrganizationScenarioStageDefinition Stage(string code, string name) => new(code, name);

    /// <summary> Создаёт определение шаблона документа сценария. </summary>
    private static OrganizationScenarioDocumentDefinition Document(string name, string kindCode) => new(name, kindCode);
}
