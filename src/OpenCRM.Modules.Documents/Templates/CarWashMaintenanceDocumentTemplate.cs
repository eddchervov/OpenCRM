namespace OpenCRM.Modules.Documents.Templates;

/// <summary> Шаблон документа обслуживания для автомойки. </summary>
public static class CarWashMaintenanceDocumentTemplate
{
    /// <summary> Создаёт шаблон документа для автомойки. </summary>
    public static MaintenanceDocumentTemplate Create() => new(
        Code: "car-wash",
        Label: "Автомойка",
        Subtitle: "Документ по визиту, услуге мойки и оказанным работам",
        AccentColorHex: "#0284C7",
        WorkActIntro: "Исполнитель оказал услуги мойки и ухода за автомобилем, а заказчик принимает результат и итоговую стоимость визита.",
        WorkOrderIntro: "Заказчик согласует услугу мойки, время визита, состав работ и предварительную стоимость.",
        MaintenanceInfoTitle: "Автомобиль, клиент и визит",
        OrderInfoTitle: "Автомобиль и запись на мойку",
        CompletedWorksTitle: "Оказанные услуги и расходные материалы",
        PlannedWorksTitle: "Планируемые услуги и расходные материалы",
        ActSummaryTitle: "Итого по визиту",
        WorkOrderSummaryTitle: "Предварительная стоимость визита",
        WorkOrderNotice: "Подпись заказчика подтверждает согласование услуги, времени визита и предварительной стоимости.");
}
