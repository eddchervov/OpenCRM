using OpenCRM.Domain.Identity;

namespace OpenCRM.Domain;

/// <summary> Операция создания платежа. </summary>
public sealed class PaymentCreationOperation
{
    /// <summary> Идентификатор операции создания платежа. </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary> Ключ идемпотентности операции создания платежа. </summary>
    public string IdempotenceKey { get; set; } = string.Empty;

    /// <summary> Идентификатор пользователя, инициировавшего операцию. </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary> Источник денежных операций: прямой адаптер или платёжный микросервис. </summary>
    public string PaymentProviderCode { get; set; } = "yookassa";

    /// <summary> Пользователь, инициировавший операцию. </summary>
    public ApplicationUser User { get; set; } = null!;

    /// <summary> Идентификатор тарифного плана. </summary>
    public int SubscriptionPlanId { get; set; }

    /// <summary> Тарифный план операции создания платежа. </summary>
    public SubscriptionPlan SubscriptionPlan { get; set; } = null!;

    /// <summary> Тип операции создания платежа. </summary>
    public string OperationType { get; set; } = string.Empty;

    /// <summary> Сумма платежа. </summary>
    public decimal Amount { get; set; }

    /// <summary> Первоначальная сумма до скидки. </summary>
    public decimal? OriginalAmount { get; set; }

    /// <summary> Сумма скидки по промокоду. </summary>
    public decimal? PromoDiscountAmount { get; set; }

    /// <summary> Идентификатор применённого промокода. </summary>
    public int? PromoCodeId { get; set; }

    /// <summary> Промокод, применённый к операции. </summary>
    public PromoCode? PromoCode { get; set; }

    /// <summary> Снимок данных промокода на момент операции. </summary>
    public string? PromoCodeSnapshot { get; set; }

    /// <summary> Идентификатор применения промокода. </summary>
    public long? PromoCodeApplicationId { get; set; }

    /// <summary> Идентификатор организации. </summary>
    public Guid? OrganizationId { get; set; }

    /// <summary> Описание платежа. </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary> URL возврата после оплаты. </summary>
    public string? ReturnUrl { get; set; }

    /// <summary> Код типа покупателя. </summary>
    public string BuyerTypeCode { get; set; } = string.Empty;

    /// <summary> Наименование покупателя. </summary>
    public string? BuyerName { get; set; }

    /// <summary> ИНН покупателя. </summary>
    public string? BuyerInn { get; set; }

    /// <summary> Метка времени запроса на создание платежа. </summary>
    public DateTime RequestTimestamp { get; set; }

    /// <summary> Тело запроса на создание платежа. </summary>
    public string RequestBody { get; set; } = string.Empty;

    /// <summary> Статус операции создания платежа. </summary>
    public string Status { get; set; } = PaymentCreationOperationStatuses.Prepared;

    /// <summary> Дата и время создания операции. </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary> Дата и время первой попытки отправки. </summary>
    public DateTime? FirstAttemptAt { get; set; }

    /// <summary> Дата и время последней попытки отправки. </summary>
    public DateTime? LastAttemptAt { get; set; }

    /// <summary> Время, до которого операция захвачена в аренду обработчиком. </summary>
    public DateTime? LeaseUntil { get; set; }

    /// <summary> Токен аренды операции обработчиком. </summary>
    public Guid? LeaseToken { get; set; }

    /// <summary> Количество попыток обработки операции. </summary>
    public int AttemptCount { get; set; }

    /// <summary> Идентификатор платежа по подписке. </summary>
    public int? SubscriptionPaymentId { get; set; }

    /// <summary> Платёж по подписке, созданный операцией. </summary>
    public SubscriptionPayment? SubscriptionPayment { get; set; }

    /// <summary> Идентификатор платежа у провайдера. </summary>
    public string? ExternalPaymentId { get; set; }

    /// <summary> Ссылка на страницу оплаты. </summary>
    public string? PaymentLink { get; set; }

    /// <summary> Дата и время создания платежа у провайдера. </summary>
    public DateTime? ProviderCreatedAt { get; set; }

    /// <summary> Код последней ошибки обработки. </summary>
    public string? LastErrorCode { get; set; }
}

/// <summary> Статусы операции создания платежа. </summary>
public static class PaymentCreationOperationStatuses
{
    /// <summary> Статус: операция подготовлена. </summary>
    public const string Prepared = "prepared";

    /// <summary> Статус: операция отправляется провайдеру. </summary>
    public const string Sending = "sending";

    /// <summary> Статус: платёж создан. </summary>
    public const string Created = "created";

    /// <summary> Статус: операция отклонена. </summary>
    public const string Rejected = "rejected";

    /// <summary> Статус: операция требует ручной проверки. </summary>
    public const string ManualReview = "manual_review";
}
