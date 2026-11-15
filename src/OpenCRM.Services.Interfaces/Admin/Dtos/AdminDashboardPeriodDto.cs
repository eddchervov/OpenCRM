namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO показателей периода дашборда. </summary>
public sealed class AdminDashboardPeriodDto
{
    /// <summary> Длительность периода в днях. </summary>
    public int Days { get; init; }

    /// <summary> Новых пользователей за период. </summary>
    public int NewUsers { get; init; }

    /// <summary> Новых организаций за период. </summary>
    public int NewOrganizations { get; init; }

    /// <summary> Количество платежей за период. </summary>
    public int PaymentsCount { get; init; }

    /// <summary> Сумма платежей за период. </summary>
    public decimal PaymentsAmount { get; init; }

    /// <summary> Количество писем за период. </summary>
    public int Emails { get; init; }

    /// <summary> Количество отправленных писем. </summary>
    public int SentEmails { get; init; }

    /// <summary> Количество неуспешных писем. </summary>
    public int FailedEmails { get; init; }

    /// <summary> Количество сообщений поддержки. </summary>
    public int SupportMessages { get; init; }

    /// <summary> Количество предложений справочника. </summary>
    public int CatalogSuggestions { get; init; }
}
