using System.Text.Json;

namespace OpenCRM.WebApp.Infrastructure.AiPilot;

public sealed record AiPilotActionParameter(string Name, string Type, string Description, bool Required);

public sealed record AiPilotActionDefinition(
    string Action,
    string Title,
    string Description,
    bool RequiresPlate,
    bool Destructive,
    IReadOnlyList<AiPilotActionParameter> Parameters);

public static class AiPilotActionCatalog
{
    public static IReadOnlyList<AiPilotActionDefinition> All { get; } = CreateActions();

    public static bool TryGet(string action, out AiPilotActionDefinition definition)
    {
        definition = All.FirstOrDefault(item => string.Equals(item.Action, action, StringComparison.Ordinal))!;

        return definition != null;
    }

    public static bool HasValidParameters(AiPilotActionCommand command, bool requireComplete = true)
    {
        var parameters = command.Parameters ?? [];
        if (parameters.Count > 80)
        {
            return false;
        }

        foreach (var item in parameters)
        {
            var definition = command.Definition.Parameters.FirstOrDefault(parameter =>
                string.Equals(parameter.Name, item.Key, StringComparison.Ordinal));
            if (definition == null)
            {
                var knownParameter = All.SelectMany(action => action.Parameters)
                    .FirstOrDefault(parameter => string.Equals(parameter.Name, item.Key, StringComparison.Ordinal));
                if (knownParameter == null || item.Value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
                {
                    return false;
                }

                continue;
            }

            if (!IsValidValue(item.Value, definition, !requireComplete))
            {
                return false;
            }
        }

        return !requireComplete || command.Definition.Parameters
            .Where(parameter => parameter.Required)
            .All(parameter => parameters.TryGetValue(parameter.Name, out var value) && IsPresent(value));
    }

    public static void RemoveNullParameters(AiPilotActionCommand command)
    {
        var nullNames = command.Parameters
            .Where(parameter => parameter.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            .Select(parameter => parameter.Key)
            .ToArray();

        foreach (var name in nullNames)
        {
            command.Parameters.Remove(name);
        }
    }

    private static bool IsValidValue(JsonElement value, AiPilotActionParameter parameter, bool allowMissingRequired = false)
    {
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return !parameter.Required || allowMissingRequired;
        }

        return parameter.Type switch
        {
            "string" => value.ValueKind == JsonValueKind.String && value.GetString()!.Length <= 1000,
            "number" => value.ValueKind == JsonValueKind.Number
                && value.TryGetDecimal(out var number)
                && number is >= -10_000_000 and <= 10_000_000,
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _),
            _ => false
        };
    }

    private static bool IsPresent(JsonElement value) => value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined
        && (value.ValueKind != JsonValueKind.String || !string.IsNullOrWhiteSpace(value.GetString()));

    private static IReadOnlyList<AiPilotActionDefinition> CreateActions()
    {
        return
        [
            A("visit", "Выполненный визит", "Записать новую уже выполненную работу и оплату.", true, false,
                [P("vehicleKind", "integer", "Тип техники: 1 легковой, 2 грузовой, 3 мотоцикл, 4 квадроцикл, 5 багги, 6 снегоход, 7 гидроцикл, 8 лодка, 9 спецтехника, 10 прицеп, 99 другая. Если тип не назван — null.")]),
            A("book", "Новая запись", "Запланировать запись автомобиля на дату и время.", true, false,
                [P("locationName", "string", "Название или номер поста, если сотрудник его назвал. Номер записывай цифрами."),
                 P("vehicleKind", "integer", "Тип техники: 1 легковой, 2 грузовой, 3 мотоцикл, 4 квадроцикл, 5 багги, 6 снегоход, 7 гидроцикл, 8 лодка, 9 спецтехника, 10 прицеп, 99 другая. Если тип не назван — null.")]),
            A("arrive", "Приёмка автомобиля", "Найти ближайшую незавершённую запись и принять автомобиль в работу.", true),
            A("start", "Начать работу", "Начать существующий заказ.", true),
            A("complete", "Завершить работу", "Завершить существующий заказ после указания выполненных работ и их стоимости.", true),
            A("reschedule", "Перенести запись", "Перенести существующую запись на новую дату или время.", true, false, [P("plannedEndDate", "string", "Явно названная дата окончания yyyy-MM-dd.")]),
            A("unsupported", "Другая команда", "Запрос не относится к доступным действиям рабочего пространства.", false),
            A("maintenance_get", "Посмотреть заказ", "Найти и прочитать заказ по госномеру или конкретной записи.", true, false, [P("maintenanceId", "string", "Известный пользователю идентификатор конкретной записи."), P("latest", "boolean", "Найти последнюю запись, только если сотрудник явно сказал «последняя» или «текущая»."), P("recordDate", "string", "Дата записи, если она помогает различить несколько заказов.")]),
            A("maintenance_update", "Изменить заказ", "Изменить примечание, даты, скидку, себестоимость труда или исполнителя заказа.", true, false, [P("maintenanceId", "string", "Известный пользователю идентификатор конкретной записи."), P("latest", "boolean", "Выбрать последнюю запись, только если сотрудник сказал «последняя»."), P("recordDate", "string", "Дата записи для выбора нужного заказа."), P("comment", "string", "Новое примечание к заказу."), P("mileage", "number", "Текущий пробег автомобиля."), P("completedDate", "string", "Дата выполнения заказа в формате yyyy-MM-dd."), P("plannedEndDate", "string", "Плановая дата окончания в формате yyyy-MM-dd."), P("discountAmount", "number", "Сумма скидки в валюте заказа."), P("laborCostAmount", "number", "Себестоимость работы в валюте заказа."), P("performer", "string", "Имя исполнителя заказа."), P("clearTime", "boolean", "Явно убрать время начала и окончания, оставив запись на дату."), P("clearMileage", "boolean", "Явно убрать указанный пробег.")] ),
            A("maintenance_delete", "Удалить заказ", "Удалить выбранный заказ после показа его даты и госномера.", true, true, [P("maintenanceId", "string", "Идентификатор конкретной записи."), P("recordDate", "string", "Дата заказа для однозначного выбора.")]),
            A("service_add", "Добавить работу", "Добавить строку услуги в заказ, сохранив остальные работы, цены, НДС, исполнителей и количество.", true, false, [P("maintenanceId", "string", "Идентификатор конкретной записи."), P("recordDate", "string", "Дата записи для выбора заказа."), P("serviceName", "string", "Название новой работы.", true), P("price", "number", "Цена работы в валюте заказа."), P("quantity", "number", "Количество работ, по умолчанию один."), P("description", "string", "Описание работы."), P("vatRateCode", "string", "Ставка НДС, только если сотрудник её назвал."), P("employeeName", "string", "Имя исполнителя работы.")]),
            A("service_update", "Изменить работу", "Изменить выбранную строку работы в заказе.", true, false, [P("maintenanceId", "string", "Идентификатор конкретной записи."), P("recordDate", "string", "Дата записи для выбора заказа."), P("serviceName", "string", "Текущее название работы."), P("lineNumber", "number", "Порядковый номер строки, если работ с одним названием несколько."), P("newName", "string", "Новое название работы."), P("price", "number", "Новая цена в валюте заказа."), P("quantity", "number", "Новое количество."), P("description", "string", "Новое описание."), P("vatRateCode", "string", "Ставка НДС, если её требуется изменить."), P("employeeName", "string", "Исполнитель работы.")]),
            A("service_remove", "Удалить работу", "Удалить только выбранную строку услуги из заказа.", true, true, [P("maintenanceId", "string", "Идентификатор конкретной записи."), P("recordDate", "string", "Дата записи для выбора заказа."), P("serviceName", "string", "Точное название удаляемой работы."), P("lineNumber", "number", "Порядковый номер одинаковой работы.")]),
            A("services_replace", "Заменить список работ", "Заменить полный список работ. Перед выполнением покажи удаляемые и добавляемые строки.", true, true, [P("maintenanceId", "string", "Идентификатор конкретной записи."), P("recordDate", "string", "Дата записи для выбора заказа.")]),
            Workflow("cancel", "Отменить заказ", "Отменить существующий заказ.", true, [P("reason", "string", "Причина отмены.")]),
            Workflow("reopen", "Возобновить отменённую запись", "Вернуть отменённый заказ в запланированные с новой датой. Выполненный заказ возвращается командой return_to_work.", true),
            Workflow("return_to_planned", "Вернуть в запланированные", "Вернуть заказ из работы в запланированное состояние.", true),
            Workflow("return_to_work", "Вернуть в работу", "Возобновить работу над заказом.", true),
            Workflow("mark_ready", "Отметить готовность", "Пометить автомобиль как готовый к выдаче.", true),
            Workflow("handover", "Выдать автомобиль", "Отметить фактическую выдачу автомобиля клиенту.", true),
            Workflow("undo_handover", "Отменить выдачу", "Вернуть автомобиль из статуса выданного.", true),
            Workflow("simple_handover", "Быстрая выдача", "Завершить выдачу автомобиля по упрощённому сценарию.", true),
            A("payment_add", "Принять оплату", "Добавить подтверждённую сотрудником оплату к выбранному заказу.", true, false, [P("maintenanceId", "string", "Идентификатор конкретной записи."), P("recordDate", "string", "Дата заказа для выбора записи."), P("payerName", "string", "Имя плательщика, если сотрудник его назвал."), P("amount", "number", "Сумма принимаемой оплаты в валюте заказа; можно также использовать paidAmount.")]),
            A("payment_cancel", "Отменить оплату", "Отменить выбранную ранее оплату с указанной причиной.", true, true, [P("maintenanceId", "string", "Идентификатор конкретной записи."), P("paymentNumber", "number", "Порядковый номер оплаты, если у заказа несколько платежей."), P("amount", "number", "Сумма отменяемой оплаты, если известна."), P("reason", "string", "Причина отмены оплаты.", true)]),
            A("assign_employee", "Назначить сотрудника", "Назначить сотрудника на заказ.", true, false, [P("maintenanceId", "string", "Идентификатор конкретной записи."), P("employeeName", "string", "Имя сотрудника; пустая строка снимает назначение.")]),
            A("assign_location", "Назначить пост", "Назначить пост или место выполнения работ.", true, false, [P("maintenanceId", "string", "Идентификатор конкретной записи."), P("locationName", "string", "Название поста; пустая строка снимает назначение.")]),
            A("intake_update", "Изменить приёмку", "Изменить пробег, комплектность, видимые повреждения или примечание приёмки.", true, false, [P("maintenanceId", "string", "Идентификатор конкретной записи."), P("recordDate", "string", "Дата заказа для выбора записи."), P("mileage", "number", "Пробег автомобиля."), P("completeness", "string", "Комплектность автомобиля."), P("visibleDamage", "string", "Видимые повреждения."), P("notes", "string", "Примечание к приёмке.")]),
            A("vehicle_get", "Найти автомобиль", "Найти автомобиль по полному госномеру.", true),
            A("vehicle_create", "Создать автомобиль", "Создать автомобиль, при необходимости связав его с найденным клиентом.", true, false, VehicleFields()),
            A("vehicle_update", "Изменить автомобиль", "Изменить данные автомобиля, найденного по текущему госномеру.", true, false, VehicleFields()),
            A("vehicle_archive", "Архивировать автомобиль", "Архивировать автомобиль после подтверждения госномера.", true, true, [P("reason", "string", "Причина архивации.")]),
            A("vehicle_restore", "Восстановить автомобиль", "Восстановить ранее архивированный автомобиль.", true),
            A("client_get", "Найти клиента", "Найти клиента по имени, телефону или связанному госномеру.", false, false, ClientLookupFields()),
            A("client_create", "Создать клиента", "Создать клиента на основе явно продиктованных данных.", false, false, [.. ClientFields(), P("name", "string", "Полное имя клиента.", true)]),
            A("client_update", "Изменить клиента", "Изменить данные найденного клиента.", false, false, ClientFields()),
            A("client_comment", "Изменить примечание клиента", "Добавить или заменить примечание в карточке клиента.", false, false, [.. ClientLookupFields(), P("comment", "string", "Новый текст примечания; пустая строка очищает его.")]),
            A("catalog_list", "Найти услугу в справочнике", "Найти услугу или товар в справочнике компании.", false, false, [P("catalogKind", "string", "Вид справочника: услуга или материал."), P("name", "string", "Название для поиска.")]),
            A("catalog_create", "Добавить позицию справочника", "Добавить услугу, набор услуг или материал в справочник компании.", false, false, CatalogFields()),
            A("catalog_update", "Изменить позицию справочника", "Изменить найденную позицию справочника компании.", false, false, [.. CatalogFields(), P("newName", "string", "Новое название."), P("isActive", "boolean", "Активность позиции; false означает архив.")]),
            A("catalog_delete", "Удалить позицию справочника", "Удалить найденную позицию справочника компании после подтверждения.", false, true, [P("catalogKind", "string", "Вид справочника: услуга или материал.", true), P("name", "string", "Точное название удаляемой позиции.", true)]),
            A("location_list", "Найти пост", "Найти пост обслуживания по названию.", false),
            A("location_create", "Создать пост", "Создать место или пост обслуживания.", false, false, LocationFields()),
            A("location_update", "Изменить пост", "Изменить найденное место или пост обслуживания.", false, false, LocationFields(true)),
            A("location_delete", "Удалить пост", "Удалить найденное место обслуживания.", false, true, [P("name", "string", "Точное название удаляемого поста.", true)]),
            A("employee_list", "Найти сотрудника", "Найти сотрудника по имени.", false, false, [P("employeeName", "string", "Имя сотрудника для поиска.")]),
            A("employee_create", "Добавить сотрудника", "Добавить нового сотрудника рабочего пространства.", false, false, [P("name", "string", "Имя нового сотрудника.", true), P("phone", "string", "Телефон сотрудника."), P("isActive", "boolean", "Активность сотрудника.")]),
            A("employee_update", "Изменить сотрудника", "Изменить имя, телефон или активность сотрудника.", false, false, [P("employeeName", "string", "Текущее имя сотрудника.", true), P("newName", "string", "Новое имя."), P("phone", "string", "Телефон сотрудника."), P("isActive", "boolean", "Активность сотрудника.")]),
            A("employee_deactivate", "Отключить сотрудника", "Отключить сотрудника от текущего рабочего пространства.", false, true, [P("employeeName", "string", "Точное имя сотрудника.", true)]),
            A("employee_link", "Связать сотрудника с участником", "Связать карточку сотрудника с существующим участником без изменения роли.", false, true, [P("employeeName", "string", "Имя сотрудника.", true), P("memberEmail", "string", "Email участника; пустая строка снимает связь.")]),
            A("shift_list", "Найти смену", "Найти смену сотрудника на дату.", false, false, [P("employeeName", "string", "Имя сотрудника."), P("date", "string", "Дата смены в формате yyyy-MM-dd.")]),
            A("shift_create", "Создать смену", "Создать смену сотрудника на определённое время.", false, false, ShiftFields()),
            A("shift_update", "Изменить смену", "Изменить время или примечание выбранной смены.", false, false, ShiftFields()),
            A("shift_delete", "Удалить смену", "Удалить смену выбранного сотрудника и даты.", false, true, [P("employeeName", "string", "Имя сотрудника.", true), P("date", "string", "Дата смены.", true)]),
            A("inventory_list", "Найти остаток", "Найти остаток материала на складе компании.", false, false, [P("name", "string", "Название материала.", true)]),
            A("inventory_receive", "Принять материал на склад", "Увеличить остаток материала на складе.", false, false, [P("name", "string", "Название материала.", true), P("quantity", "number", "Количество поступившего материала.", true), P("unit", "string", "Единица измерения материала."), P("packageQuantity", "number", "Количество единиц материала в упаковке."), P("unitCost", "number", "Себестоимость единицы материала."), P("comment", "string", "Комментарий к поступлению.")]),
            A("inventory_adjust", "Скорректировать остаток", "Изменить остаток материала после инвентаризации.", false, false, [P("name", "string", "Название материала.", true), P("quantityOnHand", "number", "Новое количество на складе.", true), P("comment", "string", "Причина корректировки.", true), P("unit", "string", "Единица нового количества и цены."), P("unitCost", "number", "Новая себестоимость за указанную единицу.")]),
            A("inventory_issue", "Списать материал", "Уменьшить остаток материала на складе.", false, true, [P("name", "string", "Название материала.", true), P("quantity", "number", "Количество списываемого материала.", true), P("comment", "string", "Причина списания."), P("unit", "string", "Единица количества: шт, л, мл, кг, г, м, м2, компл или уп.")]),
            A("material_add", "Добавить материал в заказ", "Добавить расход материала к работе в выбранном заказе.", true, false, [P("name", "string", "Название материала.", true), P("quantity", "number", "Количество материала.", true), P("serviceName", "string", "Работа, к которой относится материал."), P("lineNumber", "number", "Номер строки одинаковой работы."), P("issueNumber", "number", "Номер строки уже использованного материала."), P("unit", "string", "Единица расхода: шт, л, мл, кг, г, м, м2, компл или уп.")]),
            A("material_remove", "Вернуть материал из заказа", "Полностью вернуть выбранное списание материала заказа.", true, true, [P("name", "string", "Название материала."), P("serviceName", "string", "Название работы."), P("lineNumber", "number", "Номер работы."), P("issueNumber", "number", "Номер списания."), P("reason", "string", "Причина полного возврата списания.", true)]),
            A("defect_list", "Найти неисправность", "Найти неисправность автомобиля по названию.", true, false, DefectLookupFields()),
            A("defect_create", "Добавить неисправность", "Записать неисправность автомобиля.", true, false, DefectFields()),
            A("defect_update", "Изменить неисправность", "Изменить найденную неисправность автомобиля.", true, false, DefectFields(true)),
            A("defect_close", "Закрыть неисправность", "Закрыть ранее зарегистрированную неисправность.", true, false, DefectLookupFields()),
            A("defect_delete", "Удалить неисправность", "Удалить выбранную неисправность автомобиля.", true, true, DefectLookupFields()),
            A("work_start", "Начать операцию", "Начать учёт времени по выбранной работе.", true, false, WorkFields()),
            A("work_pause", "Приостановить операцию", "Приостановить учёт времени по работе.", true, false, WorkFields()),
            A("work_finish", "Завершить операцию", "Завершить учёт времени по выбранной работе.", true, false, WorkFields()),
            A("work_time", "Записать время операции", "Записать затраченное время по выбранной работе в минутах.", true, false, [.. WorkFields(), P("minutes", "number", "Сколько минут заняла работа.", true)]),
            A("work_report", "Добавить отчёт по работе", "Добавить текст замечания или предложения дополнительной работы. Согласование и цена оформляются отдельной командой.", true, false, [.. WorkFields(), P("kind", "string", "comment — комментарий; defect — дефект; extra — допработа; time — вопрос времени."), P("text", "string", "Текст отчёта.", true)]),
            A("work_decision", "Согласовать замечание или допработу", "Подтвердить или отклонить ранее записанное предложение.", true, true, [P("reportNumber", "number", "Номер сообщения."), P("decision", "string", "approved — согласовать, rejected — отклонить.", true), P("price", "number", "Цена допработы."), P("vatRateCode", "string", "Явно названная ставка НДС."), P("reason", "string", "Причина решения.")]),
            A("report", "Посмотреть отчёт", "Открыть финансовый или операционный отчёт за период.", false, false, [P("reportKind", "string", "Вид отчёта, например финансы или выручка."), P("dateFrom", "string", "Начальная дата периода."), P("dateTo", "string", "Конечная дата периода.")]),
            A("schedule", "Посмотреть расписание", "Открыть расписание рабочего пространства за период.", false, false, [P("dateFrom", "string", "Начальная дата периода."), P("dateTo", "string", "Конечная дата периода.")]),
            A("shift_publish", "Опубликовать смены", "Опубликовать график смен сотрудников на выбранную дату.", false, false, [P("date", "string", "Дата, на которую публикуется расписание.", true)]),
            A("shift_copy", "Скопировать график смен", "Скопировать смены на указанную дату.", false, false, [P("date", "string", "Дата назначения копируемого графика.", true)]),
            A("counter_add", "Записать показание", "Добавить новое показание счётчика автомобиля.", true, false, [P("value", "number", "Новое значение счётчика.", true), P("recordedAt", "string", "Дата и время показания."), P("comment", "string", "Комментарий к показанию.")]),
            A("counter_remove", "Удалить показание", "Удалить выбранное показание счётчика автомобиля.", true, true, [P("entryNumber", "number", "Порядковый номер показания.")])
        ];
    }

    private static AiPilotActionDefinition Workflow(string action, string title, string description, bool requiresPlate, IReadOnlyList<AiPilotActionParameter>? parameters = null) =>
        A(action, title, description, requiresPlate, false, parameters ?? []);

    private static AiPilotActionDefinition A(
        string action,
        string title,
        string description,
        bool requiresPlate,
        bool destructive = false,
        IReadOnlyList<AiPilotActionParameter>? parameters = null) =>
        new(action, title, description, requiresPlate, destructive,
            requiresPlate ? AddMaintenanceTargetParameters(parameters ?? []) : parameters ?? []);

    private static AiPilotActionParameter P(string name, string type, string description, bool required = false) =>
        new(name, type, description, required);

    private static IReadOnlyList<AiPilotActionParameter> AddMaintenanceTargetParameters(IReadOnlyList<AiPilotActionParameter> parameters)
    {
        var names = parameters.Select(parameter => parameter.Name).ToHashSet(StringComparer.Ordinal);
        var result = parameters.ToList();
        if (!names.Contains("maintenanceId"))
        {
            result.Add(P("maintenanceId", "string", "Известный пользователю идентификатор конкретной записи."));
        }

        if (!names.Contains("recordDate"))
        {
            result.Add(P("recordDate", "string", "Дата записи для выбора конкретного заказа."));
        }

        if (!names.Contains("latest"))
        {
            result.Add(P("latest", "boolean", "Выбрать последнюю запись только если сотрудник явно об этом сказал."));
        }

        return result;
    }

    private static IReadOnlyList<AiPilotActionParameter> VehicleFields() =>
    [
        P("vehicleKind", "integer", "Тип техники: 1 легковой, 2 грузовой, 3 мотоцикл, 4 квадроцикл, 5 багги, 6 снегоход, 7 гидроцикл, 8 лодка, 9 спецтехника, 10 прицеп, 99 другая. Если тип не назван — null."),
        P("name", "string", "Отображаемое название автомобиля."), P("newRegistrationPlate", "string", "Новый госномер, если его меняют."),
        P("vin", "string", "VIN, только если назван сотрудником."), P("yearIssue", "number", "Год выпуска."), P("color", "string", "Цвет автомобиля."),
        P("manualBrandName", "string", "Марка автомобиля."), P("manualModelName", "string", "Модель автомобиля."), P("description", "string", "Примечание об автомобиле."),
        P("clientName", "string", "Имя владельца для связывания с найденным клиентом."), P("phone", "string", "Телефон клиента, если он продиктован.")
    ];

    private static IReadOnlyList<AiPilotActionParameter> ClientLookupFields() =>
        [P("clientName", "string", "Имя клиента для поиска."), P("phone", "string", "Телефон клиента для поиска.")];

    private static IReadOnlyList<AiPilotActionParameter> ClientFields() =>
    [
        .. ClientLookupFields(), P("newName", "string", "Новое полное имя клиента."), P("newPhone", "string", "Новый телефон клиента, только если сотрудник просит его изменить."),
        P("comment", "string", "Примечание о клиенте."), P("firstName", "string", "Имя."), P("lastName", "string", "Фамилия."),
        P("middleName", "string", "Отчество."), P("partyTypeCode", "string", "person — физическое лицо; company — компания."), P("companyTypeCode", "string", "Для компании: legal_entity — юрлицо; entrepreneur — ИП."), P("inn", "string", "ИНН."),
        P("kpp", "string", "КПП."), P("legalAddress", "string", "Юридический адрес."), P("statusCode", "string", "Статус карточки клиента."),
        P("tagNamesText", "string", "Список текстовых тегов клиента.")
    ];

    private static IReadOnlyList<AiPilotActionParameter> CatalogFields() =>
        [P("catalogKind", "string", "service — услуга; service_bundle — набор; material — материал; work_type — вид работ; document — документ; planning_rule — правило.", true),
        P("name", "string", "Название позиции.", true), P("price", "number", "Цена."), P("durationMinutes", "number", "Длительность в минутах."),
        P("description", "string", "Описание."), P("sortOrder", "number", "Порядок в списке."), P("serviceNames", "string", "Названия услуг набора через точку с запятой.")];

    private static IReadOnlyList<AiPilotActionParameter> LocationFields(bool isUpdate = false) =>
        [P("name", "string", isUpdate ? "Текущее название поста." : "Название нового поста.", !isUpdate), P("newName", "string", "Новое название поста."), P("unitPrice", "number", "Цена за единицу расчёта."), P("durationMinutes", "number", "Продолжительность в минутах."), P("sortOrder", "number", "Порядок поста в списке."), P("isActive", "boolean", "Активность поста."), P("description", "string", "Описание поста.")];

    private static IReadOnlyList<AiPilotActionParameter> ShiftFields() =>
        [P("employeeName", "string", "Имя сотрудника."), P("shiftNumber", "number", "Порядковый номер смены, если их несколько."), P("date", "string", "Дата смены в формате yyyy-MM-dd."), P("dateFrom", "string", "Начало периода смен."), P("dateTo", "string", "Конец периода смен."), P("startTime", "string", "Время начала смены HH:mm."), P("endTime", "string", "Время окончания смены HH:mm."), P("plannedBreakMinutes", "number", "Плановый перерыв в минутах."), P("reason", "string", "Причина изменения смены."), P("comment", "string", "Примечание к смене.")];

    private static IReadOnlyList<AiPilotActionParameter> DefectLookupFields() =>
        [P("maintenanceId", "string", "Идентификатор заказа, если неисправность найдена в нём."), P("name", "string", "Название неисправности для поиска.")];

    private static IReadOnlyList<AiPilotActionParameter> DefectFields(bool isUpdate = false) =>
        [P("maintenanceId", "string", "Идентификатор заказа."), P("name", "string", "Точное название неисправности.", true), P("newName", "string", "Новое название неисправности."), P("description", "string", "Подробности неисправности."), P("severityCode", "string", "Степень серьёзности."), P("statusCode", "string", "Статус неисправности.")];

    private static IReadOnlyList<AiPilotActionParameter> WorkFields() =>
        [P("maintenanceId", "string", "Идентификатор заказа."), P("serviceName", "string", "Название работы."), P("lineNumber", "number", "Номер строки работы, если одинаковые названия повторяются."), P("reason", "string", "Причина изменения.")];
}

public sealed record AiPilotActionCommand(AiPilotActionDefinition Definition, Dictionary<string, JsonElement> Parameters);
