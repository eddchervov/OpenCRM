namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> Блок показателей, требующих внимания на дашборде. </summary>
public sealed class AdminDashboardAttentionDto
{
    /// <summary> Количество новых сообщений поддержки. </summary>
    public int NewSupportMessages { get; init; }

    /// <summary> Количество необработанных предложений в справочник. </summary>
    public int PendingCatalogSuggestions { get; init; }

    /// <summary> Количество необработанных приглашений. </summary>
    public int PendingInvitations { get; init; }

    /// <summary> Количество просроченных задач. </summary>
    public int OverdueTasks { get; init; }

    /// <summary> Количество позиций с низким запасом. </summary>
    public int LowStockItems { get; init; }

    /// <summary> Количество неуспешных платежей. </summary>
    public int FailedPayments { get; init; }

    /// <summary> Количество неуспешных писем за 7 дней. </summary>
    public int FailedEmails7Days { get; init; }

    /// <summary> Количество писем в ожидании отправки. </summary>
    public int PendingEmails { get; init; }
}

