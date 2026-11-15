namespace OpenCRM.Modules.Documents.Templates;

/// <summary> Шаблон документа обслуживания для оклейки техники. </summary>
public static class WrappingMaintenanceDocumentTemplate
{
    /// <summary> Создаёт шаблон документа для оклейки техники. </summary>
    public static MaintenanceDocumentTemplate Create() => new(
        Code: "wrapping",
        Label: "Студия оклейки",
        Subtitle: "Документ по оклейке, материалам, зонам работ и выдаче результата",
        AccentColorHex: "#C026D3",
        WorkActIntro: "Исполнитель завершил работы по оклейке, а заказчик принимает результат, зоны работ, материалы и итоговый объем проекта.",
        WorkOrderIntro: "Заказчик согласует зоны оклейки, материалы, подготовительные работы и предварительную стоимость проекта.",
        MaintenanceInfoTitle: "Объект, зоны работ и проект",
        OrderInfoTitle: "Объект и заказ на оклейку",
        CompletedWorksTitle: "Выполненные зоны, материалы и этапы оклейки",
        PlannedWorksTitle: "Планируемые зоны, материалы и этапы оклейки",
        ActSummaryTitle: "Итого по оклейке",
        WorkOrderSummaryTitle: "Предварительная стоимость оклейки",
        WorkOrderNotice: "Подпись заказчика подтверждает согласование зон работ, материалов, подготовки и предварительной стоимости.");
}
