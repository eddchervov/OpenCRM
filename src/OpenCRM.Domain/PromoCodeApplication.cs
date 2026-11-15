namespace OpenCRM.Domain;

/// <summary> Применение промокода организацией. </summary>
public class PromoCodeApplication
{
    /// <summary> Идентификатор применения промокода. </summary>
    public long Id { get; set; }

    /// <summary> Идентификатор промокода. </summary>
    public int PromoCodeId { get; set; }

    /// <summary> Применённый промокод. </summary>
    public PromoCode PromoCode { get; set; } = null!;

    /// <summary> Идентификатор организации. </summary>
    public Guid OrganizationId { get; set; }

    /// <summary> Организация, к которой применён промокод. </summary>
    public Organization Organization { get; set; } = null!;

    /// <summary> Идентификатор операции создания платежа. </summary>
    public Guid? PaymentCreationOperationId { get; set; }

    /// <summary> Операция создания платежа, связанная с применением. </summary>
    public PaymentCreationOperation? PaymentCreationOperation { get; set; }

    /// <summary> Идентификатор платежа по подписке. </summary>
    public int? SubscriptionPaymentId { get; set; }

    /// <summary> Платёж по подписке, связанный с применением. </summary>
    public SubscriptionPayment? SubscriptionPayment { get; set; }

    /// <summary> Идентификатор пользователя, применившего промокод. </summary>
    public string ApplyActorUserId { get; set; } = string.Empty;

    /// <summary> Код типа выгоды применения промокода. </summary>
    public string BenefitTypeCode { get; set; } = string.Empty;

    /// <summary> Величина скидки при применении промокода. </summary>
    public decimal? DiscountValue { get; set; }
    // Срок использования закреплённой денежной скидки; null — бессрочно.
    /// <summary> Дата окончания действия скидки по применению. </summary>
    public DateTime? DiscountValidUntil { get; set; }

    /// <summary> Количество бонусных дней по применению промокода. </summary>
    public int? BonusDays { get; set; }

    /// <summary> Статус применения промокода. </summary>
    public string Status { get; set; } = PromoCodeApplicationStatuses.Assigned;

    /// <summary> Дата и время применения промокода. </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary> Дата и время использования применения промокода. </summary>
    public DateTime? ConsumedAt { get; set; }
    // Выданная по промокоду подписка для немедленного подарочного доступа.
    /// <summary> Идентификатор выданной подписки. </summary>
    public int? GrantedSubscriptionId { get; set; }

    /// <summary> Подписка, выданная по применению промокода. </summary>
    public OrganizationSubscription? GrantedSubscription { get; set; }
    // Историческое окончание доступа после выдачи подарочной подписки.
    /// <summary> Дата окончания доступа по применению промокода. </summary>
    public DateTime? AccessUntil { get; set; }
}

/// <summary> Статусы применения промокода. </summary>
public static class PromoCodeApplicationStatuses
{
    /// <summary> Статус: промокод назначен. </summary>
    public const string Assigned = "assigned";

    /// <summary> Статус: промокод активирован. </summary>
    public const string Claimed = "claimed";

    /// <summary> Статус: промокод использован. </summary>
    public const string Consumed = "consumed";
}
