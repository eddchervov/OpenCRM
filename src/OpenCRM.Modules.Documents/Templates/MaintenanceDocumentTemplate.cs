namespace OpenCRM.Modules.Documents.Templates;

/// <summary> Настройки оформления документа обслуживания: коды, цвета и тексты разделов. </summary>
public sealed record MaintenanceDocumentTemplate(
    string Code,
    string Label,
    string Subtitle,
    string AccentColorHex,
    string WorkActIntro,
    string WorkOrderIntro,
    string MaintenanceInfoTitle,
    string OrderInfoTitle,
    string CompletedWorksTitle,
    string PlannedWorksTitle,
    string ActSummaryTitle,
    string WorkOrderSummaryTitle,
    string WorkOrderNotice)
{
    /// <summary> Возвращает акцентный цвет в формате, совместимом с Word. </summary>
    public string AccentColorWord => AccentColorHex.TrimStart('#');
}
