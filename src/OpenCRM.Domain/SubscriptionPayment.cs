using OpenCRM.Domain.Identity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenCRM.Domain;

/// <summary> Платёж за подписку. </summary>
public class SubscriptionPayment
{
    /// <summary> Идентификатор платежа. </summary>
    public int Id { get; set; }

    /// <summary> Идентификатор пользователя, инициировавшего платёж. </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary> Источник денежных операций: прямой адаптер или платёжный микросервис. </summary>
    public string PaymentProviderCode { get; set; } = "yookassa";

    /// <summary> Пользователь, инициировавший платёж. </summary>
    public ApplicationUser User { get; set; } = null!;

    /// <summary> Идентификатор тарифного плана. </summary>
    public int SubscriptionPlanId { get; set; }

    /// <summary> Тарифный план платежа. </summary>
    public SubscriptionPlan SubscriptionPlan { get; set; } = null!;

    /// <summary> Сумма платежа. </summary>
    public decimal Amount { get; set; }

    /// <summary> Возвращённая сумма по платежу. </summary>
    public decimal RefundedAmount { get; set; }

    /// <summary> Первоначальная сумма до скидки. </summary>
    public decimal? OriginalAmount { get; set; }

    /// <summary> Сумма скидки по промокоду. </summary>
    public decimal? PromoDiscountAmount { get; set; }

    /// <summary> Идентификатор применённого промокода. </summary>
    public int? PromoCodeId { get; set; }

    /// <summary> Промокод, применённый к платежу. </summary>
    public PromoCode? PromoCode { get; set; }

    /// <summary> Снимок данных промокода на момент платежа. </summary>
    public string? PromoCodeSnapshot { get; set; }

    /// <summary> Идентификатор применения промокода. </summary>
    public long? PromoCodeApplicationId { get; set; }

    /// <summary> Дата и время создания платежа. </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary> Дата и время последнего изменения статуса. </summary>
    public DateTime? StatusUpdatedAt { get; set; }

    /// <summary> Дата и время оплаты. </summary>
    public DateTime? PaidAt { get; set; }

    /// <summary> Дата и время последней сверки с провайдером. </summary>
    public DateTime? LastReconciledAt { get; set; }

    /// <summary> Способ оплаты. </summary>
    public string PaymentMethod { get; set; } = string.Empty;

    /// <summary> Статус платежа. </summary>
    public string PaymentStatus { get; set; } = string.Empty;

    /// <summary> Идентификатор транзакции. </summary>
    public string? TransactionId { get; set; }

    /// <summary> Ссылка на страницу оплаты. </summary>
    public string? PaymentLink { get; set; }

    /// <summary> Идентификатор платежа у провайдера. </summary>
    public string? ExternalPaymentId { get; set; }

    /// <summary> Код типа покупателя. </summary>
    public string BuyerTypeCode { get; set; } = "unknown";

    /// <summary> Наименование покупателя. </summary>
    public string? BuyerName { get; set; }

    /// <summary> ИНН покупателя. </summary>
    public string? BuyerInn { get; set; }

    /// <summary> Дата и время оформления чека НПД. </summary>
    public DateTime? NpdReceiptIssuedAt { get; set; }

    /// <summary> Идентификатор пользователя, отметившего чек НПД. </summary>
    public string? NpdReceiptMarkedByUserId { get; set; }

    /// <summary> Причина оформления чека НПД. </summary>
    public string? NpdReceiptReason { get; set; }

    /// <summary> URL чека НПД. </summary>
    public string? NpdReceiptUrl { get; set; }

    /// <summary> Комментарий к чеку НПД. </summary>
    public string? NpdReceiptComment { get; set; }

    /// <summary> Путь к файлу чека НПД. </summary>
    public string? NpdReceiptFilePath { get; set; }

    /// <summary> Имя файла чека НПД. </summary>
    public string? NpdReceiptFileName { get; set; }

    /// <summary> Тип содержимого файла чека НПД. </summary>
    public string? NpdReceiptFileContentType { get; set; }

    /// <summary> Идентификатор email-сообщения с чеком НПД. </summary>
    public int? NpdReceiptEmailMessageId { get; set; }

    /// <summary> Количество попыток доставки чека НПД. </summary>
    public int NpdReceiptDeliveryAttempt { get; set; }

    /// <summary> Хеш источника доставки чека НПД. </summary>
    public string? NpdReceiptDeliverySourceHash { get; set; }
}
