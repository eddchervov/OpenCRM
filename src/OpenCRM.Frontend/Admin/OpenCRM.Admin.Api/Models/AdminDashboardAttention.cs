namespace OpenCRM.Admin.Api.Models;

/// <summary> Счётчики объектов, требующих внимания. </summary>
public sealed class AdminDashboardAttention
{
    /// <summary> Количество новых обращений в поддержку. </summary>
    public int NewSupportMessages { get; init; }

    /// <summary> Количество предложений по каталогу на рассмотрении. </summary>
    public int PendingCatalogSuggestions { get; init; }

    /// <summary> Количество приглашений на рассмотрении. </summary>
    public int PendingInvitations { get; init; }

    /// <summary> Количество просроченных задач. </summary>
    public int OverdueTasks { get; init; }

    /// <summary> Количество позиций с низким запасом. </summary>
    public int LowStockItems { get; init; }

    /// <summary> Количество неуспешных платежей. </summary>
    public int FailedPayments { get; init; }

    /// <summary> Количество неуспешных писем за 7 дней. </summary>
    public int FailedEmails7Days { get; init; }

    /// <summary> Количество писем в очереди. </summary>
    public int PendingEmails { get; init; }
}
