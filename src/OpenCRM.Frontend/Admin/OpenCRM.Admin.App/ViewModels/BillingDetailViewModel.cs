namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Модель представления деталей биллинга. </summary>
public sealed class BillingDetailViewModel(
    string title,
    string subtitle,
    IReadOnlyList<DashboardMetric> fields,
    IReadOnlyList<DashboardStatisticGroup> audits,
    string auditError,
    string auditTitle = "История изменений")
{
    /// <summary> Заголовок экрана. </summary>
    public string Title { get; } = title;

    /// <summary> Подзаголовок экрана. </summary>
    public string Subtitle { get; } = subtitle;

    /// <summary> Поля с показателями. </summary>
    public IReadOnlyList<DashboardMetric> Fields { get; } = fields;

    /// <summary> Группы audit-записей. </summary>
    public IReadOnlyList<DashboardStatisticGroup> Audits { get; } = audits;

    /// <summary> Текст ошибки загрузки audit-записей. </summary>
    public string AuditError { get; } = auditError;

    /// <summary> Заголовок раскрываемого журнала. </summary>
    public string AuditTitle { get; } = auditTitle;

    /// <summary> Признак наличия audit-записей. </summary>
    public bool HasAudits => this.Audits.Count > 0;

    /// <summary> Признак наличия ошибки audit-записей. </summary>
    public bool HasAuditError => !string.IsNullOrWhiteSpace(this.AuditError);
}
