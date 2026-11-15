namespace OpenCRM.Modules.Billing.Contracts.Dtos;

/// <summary> Ключевые показатели биллинга для админки. </summary>
public sealed class AdminBillingKpisDto
{
    /// <summary> Общее количество платежей. </summary>
    public int TotalPayments { get; init; }

    /// <summary> Сумма платежей за 30 дней. </summary>
    public decimal Payments30DaysAmount { get; init; }

    /// <summary> Количество платежей за 30 дней. </summary>
    public int Payments30DaysCount { get; init; }

    /// <summary> Количество проблемных платежей за 30 дней. </summary>
    public int ProblemPayments30Days { get; init; }

    /// <summary> Количество ожидающих платежей за 30 дней. </summary>
    public int PendingPayments30Days { get; init; }

    /// <summary> Количество активных подписок пользователей. </summary>
    public int ActiveUserSubscriptions { get; init; }

    /// <summary> Количество активных подписок организаций. </summary>
    public int ActiveOrganizationSubscriptions { get; init; }

    /// <summary> Количество подписок, истекающих в течение 7 дней. </summary>
    public int ExpiringSubscriptions7Days { get; init; }
}

