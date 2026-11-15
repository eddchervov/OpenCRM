namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO ключевых показателей дашборда. </summary>
public sealed class AdminDashboardKpisDto
{
    /// <summary> Общее количество пользователей. </summary>
    public int UsersTotal { get; init; }

    /// <summary> Новых пользователей сегодня. </summary>
    public int NewUsersToday { get; init; }

    /// <summary> Новых пользователей за 7 дней. </summary>
    public int NewUsers7Days { get; init; }

    /// <summary> Количество входов сегодня. </summary>
    public int LoginsToday { get; init; }

    /// <summary> Активные пользователи за 7 дней. </summary>
    public int ActiveUsers7Days { get; init; }

    /// <summary> Пользователи, не входившие в систему. </summary>
    public int UsersNeverLoggedIn { get; init; }

    /// <summary> Общее количество организаций. </summary>
    public int OrganizationsTotal { get; init; }

    /// <summary> Количество активных организаций. </summary>
    public int ActiveOrganizations { get; init; }

    /// <summary> Новых организаций за 7 дней. </summary>
    public int NewOrganizations7Days { get; init; }

    /// <summary> Количество участников организаций. </summary>
    public int OrganizationMembers { get; init; }

    /// <summary> Общее количество транспортных средств. </summary>
    public int VehiclesTotal { get; init; }

    /// <summary> Общее количество обслуживаний. </summary>
    public int MaintenancesTotal { get; init; }

    /// <summary> Количество запланированных обслуживаний. </summary>
    public int PlannedMaintenances { get; init; }

    /// <summary> Общее количество документов. </summary>
    public int DocumentsTotal { get; init; }

    /// <summary> Количество открытых дефектов. </summary>
    public int OpenDefects { get; init; }

    /// <summary> Сумма платежей за 30 дней. </summary>
    public decimal Payments30DaysAmount { get; init; }

    /// <summary> Количество платежей за 30 дней. </summary>
    public int Payments30DaysCount { get; init; }

    /// <summary> Количество активных подписок организаций. </summary>
    public int ActiveOrganizationSubscriptions { get; init; }

    /// <summary> Количество писем сегодня. </summary>
    public int EmailsToday { get; init; }

    /// <summary> Количество писем за 7 дней. </summary>
    public int Emails7Days { get; init; }

    /// <summary> Количество отправленных писем за 7 дней. </summary>
    public int SentEmails7Days { get; init; }

    /// <summary> Количество неуспешных писем за 7 дней. </summary>
    public int FailedEmails7Days { get; init; }

    /// <summary> Количество писем в ожидании отправки. </summary>
    public int PendingEmails { get; init; }
}

