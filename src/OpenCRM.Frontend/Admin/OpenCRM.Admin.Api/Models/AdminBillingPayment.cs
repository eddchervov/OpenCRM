namespace OpenCRM.Admin.Api.Models;

/// <summary> Платёж для административного просмотра. </summary>
public sealed class AdminBillingPayment
{
    /// <summary> Идентификатор платежа. </summary>
    public int Id { get; init; }

    /// <summary> Код провайдера платежа. </summary>
    public string PaymentProviderCode { get; init; } = string.Empty;

    /// <summary> Идентификатор исходной покупки подписки. </summary>
    public Guid? PurchaseId { get; init; }

    /// <summary> Состояние выдачи доступа по покупке. </summary>
    public string? ApplicationStatus { get; init; }

    /// <summary> Последний безопасный код ошибки обработки. </summary>
    public string? LastErrorCode { get; init; }

    /// <summary> Идентификатор пользователя. </summary>
    public string UserId { get; init; } = string.Empty;

    /// <summary> Имя пользователя. </summary>
    public string User { get; init; } = string.Empty;

    /// <summary> Email пользователя. </summary>
    public string? UserEmail { get; init; }

    /// <summary> Телефон пользователя. </summary>
    public string? UserPhone { get; init; }

    /// <summary> Идентификатор тарифного плана. </summary>
    public int SubscriptionPlanId { get; init; }

    /// <summary> Название тарифного плана. </summary>
    public string PlanName { get; init; } = string.Empty;

    /// <summary> Код типа тарифного плана. </summary>
    public string PlanTypeCode { get; init; } = string.Empty;

    /// <summary> Длительность тарифного плана в днях. </summary>
    public int PlanDurationDays { get; init; }

    /// <summary> Сумма платежа. </summary>
    public decimal Amount { get; init; }

    /// <summary> Сумма возвратов, подтверждённых платёжным сервисом. </summary>
    public decimal RefundedAmount { get; init; }

    /// <summary> Дата подтверждения оплаты. </summary>
    public DateTime? PaidAt { get; init; }

    /// <summary> Состояние платежа у провайдера. </summary>
    public string? ProviderStatus { get; init; }

    /// <summary> Дата выдачи доступа по покупке. </summary>
    public DateTime? AppliedAt { get; init; }

    /// <summary> Начало исторического периода, выданного по покупке. </summary>
    public DateTime? GrantedFrom { get; init; }

    /// <summary> Конец исторического периода, выданного по покупке. </summary>
    public DateTime? GrantedUntil { get; init; }

    /// <summary> Признак тестового платежа. </summary>
    public bool? IsTest { get; init; }

    /// <summary> Идентификатор организации, для которой сделана покупка. </summary>
    public Guid? OrganizationId { get; init; }

    /// <summary> Название организации, для которой сделана покупка. </summary>
    public string? OrganizationName { get; init; }

    /// <summary> События платёжного сервиса без конфиденциальной полезной нагрузки. </summary>
    public IReadOnlyList<AdminBillingPaymentEvent> ProviderEvents { get; init; } = [];

    /// <summary> Исходная сумма до скидки. </summary>
    public decimal? OriginalAmount { get; init; }

    /// <summary> Размер скидки по промокоду. </summary>
    public decimal? PromoDiscountAmount { get; init; }

    /// <summary> Снимок промокода на момент платежа. </summary>
    public string? PromoCodeSnapshot { get; init; }

    /// <summary> Момент создания платежа. </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary> Момент обновления статуса платежа. </summary>
    public DateTime? StatusUpdatedAt { get; init; }

    /// <summary> Код способа оплаты. </summary>
    public string? PaymentMethod { get; init; }

    /// <summary> Название способа оплаты. </summary>
    public string PaymentMethodName { get; init; } = string.Empty;

    /// <summary> Статус платежа. </summary>
    public string? PaymentStatus { get; init; }

    /// <summary> Группа статуса платежа. </summary>
    public string StatusGroup { get; init; } = string.Empty;

    /// <summary> Название статуса платежа. </summary>
    public string StatusName { get; init; } = string.Empty;

    /// <summary> Признак проблемного платежа. </summary>
    public bool IsProblem { get; init; }

    /// <summary> Идентификатор транзакции. </summary>
    public string? TransactionId { get; init; }

    /// <summary> Внешний идентификатор платежа. </summary>
    public string? ExternalPaymentId { get; init; }

    /// <summary> Ссылка на оплату. </summary>
    public string? PaymentLink { get; init; }

    /// <summary> Признак наличия внешнего идентификатора. </summary>
    public bool HasExternalId { get; init; }

    /// <summary> Признак наличия ссылки на оплату. </summary>
    public bool HasPaymentLink { get; init; }
}
