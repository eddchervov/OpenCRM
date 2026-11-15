namespace OpenCRM.Modules.Documents.Templates;

/// <summary> Шаблон документа обслуживания для автошколы. </summary>
public static class DrivingSchoolMaintenanceDocumentTemplate
{
    /// <summary> Создаёт шаблон документа для автошколы. </summary>
    public static MaintenanceDocumentTemplate Create() => new(
        Code: "driving-school",
        Label: "Автошкола",
        Subtitle: "Документ по учебной технике и регламентному обслуживанию",
        AccentColorHex: "#2563EB",
        WorkActIntro: "Обслуживание учебной техники выполнено и принято к учету для контроля готовности к занятиям и экзаменационным выездам.",
        WorkOrderIntro: "Согласован перечень работ по учебной технике, ответственному исполнителю и условиям выполнения.",
        MaintenanceInfoTitle: "Учебная техника и обслуживание",
        OrderInfoTitle: "Учебная техника и заказ",
        CompletedWorksTitle: "Выполненные работы и проверка готовности",
        PlannedWorksTitle: "Планируемые работы и проверка готовности",
        ActSummaryTitle: "Итого по обслуживанию",
        WorkOrderSummaryTitle: "Плановая стоимость обслуживания",
        WorkOrderNotice: "Подпись ответственного подтверждает согласование работ по учебной технике и условий выполнения.");
}
