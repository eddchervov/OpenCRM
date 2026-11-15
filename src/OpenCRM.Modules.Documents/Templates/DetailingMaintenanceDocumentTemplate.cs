namespace OpenCRM.Modules.Documents.Templates;

/// <summary> Шаблон документа обслуживания для детейлинга. </summary>
public static class DetailingMaintenanceDocumentTemplate
{
    /// <summary> Создаёт шаблон документа для детейлинга. </summary>
    public static MaintenanceDocumentTemplate Create() => new(
        Code: "detailing",
        Label: "Детейлинг",
        Subtitle: "Документ по проекту, этапам, материалам и приемке результата",
        AccentColorHex: "#7C3AED",
        WorkActIntro: "Исполнитель завершил детейлинг-проект, а заказчик принимает результат, использованные материалы и зафиксированный объем этапов.",
        WorkOrderIntro: "Заказчик согласует перечень этапов, материалов, сроков выдержки и предварительную стоимость детейлинг-проекта.",
        MaintenanceInfoTitle: "Объект, клиент и проект",
        OrderInfoTitle: "Объект и заявка на проект",
        CompletedWorksTitle: "Выполненные этапы, материалы и контроль качества",
        PlannedWorksTitle: "Планируемые этапы, материалы и контроль качества",
        ActSummaryTitle: "Итого по проекту",
        WorkOrderSummaryTitle: "Предварительная стоимость проекта",
        WorkOrderNotice: "Подпись заказчика подтверждает согласование этапов проекта, материалов, сроков выдержки и предварительной стоимости.");
}
