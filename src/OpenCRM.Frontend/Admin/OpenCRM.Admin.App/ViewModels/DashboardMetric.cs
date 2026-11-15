namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Метрика дашборда с заголовком и значением. </summary>
public sealed record DashboardMetric(string Title, string Value, string Detail = "", bool NeedsAttention = false)
{
    /// <summary> Признак наличия дополнительного описания. </summary>
    public bool HasDetail => !string.IsNullOrWhiteSpace(this.Detail) && this.Detail != "—";
}
