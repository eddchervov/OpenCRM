namespace OpenCRM.Modules.Documents.Templates;

/// <summary> Шаблон документа обслуживания для поддержки СТО. </summary>
public static class ServiceStationMaintenanceDocumentTemplate
{
    /// <summary> Создаёт шаблон документа для поддержки СТО. </summary>
    public static MaintenanceDocumentTemplate Create() => new(
        Code: "service-station",
        Label: "СТО / автосервис",
        Subtitle: "Документ по ремонту, диагностике и обслуживанию техники",
        AccentColorHex: "#E67337",
        WorkActIntro: "Исполнитель выполнил, а заказчик принял работы по обслуживанию техники в объеме и на условиях, указанных в акте.",
        WorkOrderIntro: "Заказчик поручает, а исполнитель принимает к выполнению работы по обслуживанию техники в объеме и на условиях, указанных в заказ-наряде.",
        MaintenanceInfoTitle: "Техника и обслуживание",
        OrderInfoTitle: "Техника и заказ",
        CompletedWorksTitle: "Выполненные работы и материалы",
        PlannedWorksTitle: "Планируемые работы и материалы",
        ActSummaryTitle: "Итого по акту",
        WorkOrderSummaryTitle: "Предварительная стоимость",
        WorkOrderNotice: "Подпись заказчика подтверждает согласование перечня работ, материалов и предварительной стоимости.");
}
