namespace OpenCRM.Modules.Documents.Templates;

/// <summary> Минимальный шаблон документа обслуживания. </summary>
public static class MinimalMaintenanceDocumentTemplate
{
    /// <summary> Создаёт минимальный шаблон документа. </summary>
    public static MaintenanceDocumentTemplate Create() => new(
        Code: "minimal",
        Label: "Деловой стиль",
        Subtitle: "Нейтральный документ по обслуживанию техники",
        AccentColorHex: "#111827",
        WorkActIntro: "Исполнитель выполнил, а заказчик принял работы в объеме и на условиях, указанных в документе.",
        WorkOrderIntro: "Заказчик поручает, а исполнитель принимает к выполнению работы в объеме и на условиях, указанных в заказ-наряде.",
        MaintenanceInfoTitle: "Техника и обслуживание",
        OrderInfoTitle: "Техника и заказ",
        CompletedWorksTitle: "Выполненные работы и материалы",
        PlannedWorksTitle: "Планируемые работы и материалы",
        ActSummaryTitle: "Итого",
        WorkOrderSummaryTitle: "Предварительная стоимость",
        WorkOrderNotice: "Подпись заказчика подтверждает согласование перечня работ, материалов и предварительной стоимости.");
}
