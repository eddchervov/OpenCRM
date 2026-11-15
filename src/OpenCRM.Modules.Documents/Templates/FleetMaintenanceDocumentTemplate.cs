namespace OpenCRM.Modules.Documents.Templates;

// HIDDEN_FLEET: сохранено для прежних организаций и согласованного возврата. См. docs/hidden-fleet-scenario.md.
/// <summary> Шаблон документа обслуживания для автопарка. </summary>
public static class FleetMaintenanceDocumentTemplate
{
    /// <summary> Создаёт шаблон документа для автопарка. </summary>
    public static MaintenanceDocumentTemplate Create() => new(
        Code: "fleet",
        Label: "Автопарк",
        Subtitle: "Документ по эксплуатации и обслуживанию техники",
        AccentColorHex: "#0F766E",
        WorkActIntro: "Работы по технике выполнены и зафиксированы для учета состояния, затрат и эксплуатационной готовности автопарка.",
        WorkOrderIntro: "Работы назначены для обслуживания техники автопарка с указанным объемом, ответственным и плановой стоимостью.",
        MaintenanceInfoTitle: "Техника и обслуживание автопарка",
        OrderInfoTitle: "Техника и заявка на обслуживание",
        CompletedWorksTitle: "Выполненные работы, расходники и замечания",
        PlannedWorksTitle: "Планируемые работы, расходники и замечания",
        ActSummaryTitle: "Итого по обслуживанию",
        WorkOrderSummaryTitle: "Плановая стоимость работ",
        WorkOrderNotice: "Подпись ответственного подтверждает согласование объема работ, расходников и плановой стоимости.");
}
