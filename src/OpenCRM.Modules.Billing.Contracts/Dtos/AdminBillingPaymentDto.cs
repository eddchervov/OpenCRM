namespace OpenCRM.Modules.Billing.Contracts.Dtos;

/// <summary> Платёж в разделе биллинга админки. </summary>
public sealed class AdminBillingPaymentDto
{
    /// <summary> Идентификатор платежа. </summary>
    public int Id { get; init; }

    /// <summary> Идентификатор пользователя, инициировавшего платёж. </summary>
    public string UserId { get; init; } = string.Empty;

    /// <summary> Имя пользователя, инициировавшего платёж. </summary>
    public string User { get; init; } = string.Empty;

    /// <summary> Адрес электронной почты пользователя, инициировавшего платёж. </summary>
    public string? UserEmail { get; init; }

    /// <summary> Телефон пользователя, инициировавшего платёж. </summary>
    public string? UserPhone { get; init; }

    /// <summary> Идентификатор тарифного плана платежа. </summary>
    public int SubscriptionPlanId { get; init; }

    /// <summary> Код сервиса, который обрабатывает платёж. </summary>
    public string PaymentProviderCode { get; init; } = string.Empty;

    /// <summary> Идентификатор покупки в платёжном сервисе. </summary>
    public Guid? PurchaseId { get; init; }

    /// <summary> Состояние применения покупки к подписке. </summary>
    public string? ApplicationStatus { get; init; }

    /// <summary> Последний безопасный код ошибки применения покупки. </summary>
    public string? LastErrorCode { get; init; }

    /// <summary> Последние безопасные записи журнала платёжного сервиса. </summary>
    public IReadOnlyList<AdminBillingPaymentEventDto> ProviderEvents { get; init; } = [];

    /// <summary> Название тарифного плана платежа. </summary>
    public string PlanName { get; init; } = string.Empty;

    /// <summary> Код типа тарифного плана платежа. </summary>
    public string PlanTypeCode { get; init; } = string.Empty;

    /// <summary> Длительность тарифного плана платежа в днях. </summary>
    public int PlanDurationDays { get; init; }

    /// <summary> Сумма платежа. </summary>
    public decimal Amount { get; init; }

    /// <summary> Подтверждённая сумма возвратов в рублях. </summary>
    public decimal RefundedAmount { get; init; }

    /// <summary> Дата подтверждения оплаты провайдером. </summary>
    public DateTime? PaidAt { get; init; }

    /// <summary> Подтверждённое денежное состояние в платёжном сервисе. </summary>
    public string? ProviderStatus { get; init; }

    /// <summary> Дата выдачи доступа по этой покупке. </summary>
    public DateTime? AppliedAt { get; init; }

    /// <summary> Начало периода, выданного по покупке, без учёта последующих ручных изменений. </summary>
    public DateTime? GrantedFrom { get; init; }

    /// <summary> Конец периода, выданного по покупке, без учёта последующих ручных изменений. </summary>
    public DateTime? GrantedUntil { get; init; }

    /// <summary> Признак тестовой покупки, если она связана с платёжным сервисом. </summary>
    public bool? IsTest { get; init; }

    /// <summary> Организация, для которой приобретён доступ. </summary>
    public Guid? OrganizationId { get; init; }

    /// <summary> Название организации, для которой приобретён доступ. </summary>
    public string? OrganizationName { get; init; }

    /// <summary> Исходная сумма платежа до скидки. </summary>
    public decimal? OriginalAmount { get; init; }

    /// <summary> Размер скидки по промокоду. </summary>
    public decimal? PromoDiscountAmount { get; init; }

    /// <summary> Промокод, зафиксированный на момент платежа. </summary>
    public string? PromoCodeSnapshot { get; init; }

    /// <summary> Дата и время создания платежа. </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary> Дата и время последнего обновления статуса платежа. </summary>
    public DateTime? StatusUpdatedAt { get; init; }

    /// <summary> Способ оплаты. </summary>
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

    /// <summary> Признак наличия внешнего идентификатора платежа. </summary>
    public bool HasExternalId { get; init; }

    /// <summary> Признак наличия ссылки на оплату. </summary>
    public bool HasPaymentLink { get; init; }
}
