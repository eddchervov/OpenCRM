namespace OpenCRM.Admin.Api.Models;

/// <summary> Статистика за анализируемый период. </summary>
public sealed class AdminDashboardPeriod
{
    /// <summary> Длительность периода в днях. </summary>
    public int Days { get; init; }

    /// <summary> Новые пользователи за период. </summary>
    public int NewUsers { get; init; }

    /// <summary> Новые организации за период. </summary>
    public int NewOrganizations { get; init; }

    /// <summary> Количество платежей за период. </summary>
    public int PaymentsCount { get; init; }

    /// <summary> Сумма платежей за период. </summary>
    public decimal PaymentsAmount { get; init; }

    /// <summary> Количество писем за период. </summary>
    public int Emails { get; init; }

    /// <summary> Количество отправленных писем за период. </summary>
    public int? SentEmails { get; init; }

    /// <summary> Количество неуспешных писем за период. </summary>
    public int? FailedEmails { get; init; }

    /// <summary> Количество обращений в поддержку за период. </summary>
    public int SupportMessages { get; init; }

    /// <summary> Количество предложений по каталогу за период. </summary>
    public int CatalogSuggestions { get; init; }
}
