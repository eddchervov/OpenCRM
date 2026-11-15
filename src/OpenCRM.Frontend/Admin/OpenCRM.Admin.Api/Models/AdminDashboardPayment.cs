namespace OpenCRM.Admin.Api.Models;

/// <summary> Платёж для административной панели. </summary>
public sealed class AdminDashboardPayment
{
    /// <summary> Идентификатор платежа. </summary>
    public int Id { get; init; }

    /// <summary> Сумма платежа. </summary>
    public decimal Amount { get; init; }

    /// <summary> Статус платежа. </summary>
    public string? PaymentStatus { get; init; }

    /// <summary> Дата платежа. </summary>
    public DateTimeOffset PaymentDate { get; init; }

    /// <summary> Название тарифного плана. </summary>
    public string Plan { get; init; } = string.Empty;

    /// <summary> Имя пользователя. </summary>
    public string User { get; init; } = string.Empty;
}
