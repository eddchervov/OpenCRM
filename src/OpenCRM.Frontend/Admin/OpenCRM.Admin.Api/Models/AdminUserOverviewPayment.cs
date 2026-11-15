namespace OpenCRM.Admin.Api.Models;

/// <summary> Платёж пользователя в обзоре. </summary>
public sealed class AdminUserOverviewPayment
{
    /// <summary> Идентификатор платежа. </summary>
    public int Id { get; init; }

    /// <summary> Сумма платежа. </summary>
    public decimal Amount { get; init; }

    /// <summary> Дата платежа. </summary>
    public DateTime PaymentDate { get; init; }

    /// <summary> Код способа оплаты. </summary>
    public string PaymentMethod { get; init; } = string.Empty;

    /// <summary> Название способа оплаты. </summary>
    public string PaymentMethodName { get; init; } = string.Empty;

    /// <summary> Код статуса платежа. </summary>
    public string PaymentStatus { get; init; } = string.Empty;

    /// <summary> Название статуса платежа. </summary>
    public string PaymentStatusName { get; init; } = string.Empty;

    /// <summary> Группа статуса платежа. </summary>
    public string PaymentStatusGroup { get; init; } = string.Empty;

    /// <summary> Идентификатор транзакции. </summary>
    public string? TransactionId { get; init; }

    /// <summary> Внешний идентификатор платежа. </summary>
    public string? ExternalPaymentId { get; init; }

    /// <summary> Ссылка на оплату. </summary>
    public string? PaymentLink { get; init; }

    /// <summary> Идентификатор тарифного плана. </summary>
    public int SubscriptionPlanId { get; init; }

    /// <summary> Название тарифного плана. </summary>
    public string PlanName { get; init; } = string.Empty;
}
