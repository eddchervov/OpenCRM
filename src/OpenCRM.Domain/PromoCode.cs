namespace OpenCRM.Domain;

/// <summary> Промокод. </summary>
public class PromoCode
{
    /// <summary> Идентификатор промокода. </summary>
    public int Id { get; set; }

    /// <summary> Код промокода. </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary> Название промокода. </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary> Описание промокода. </summary>
    public string? Description { get; set; }

    /// <summary> Признак активности промокода. </summary>
    public bool IsActive { get; set; } = true;

    /// <summary> Дата начала действия промокода. </summary>
    public DateTime? ValidFrom { get; set; }

    /// <summary> Дата окончания действия промокода. </summary>
    public DateTime? ValidUntil { get; set; }

    /// <summary> Максимальное количество активаций промокода. </summary>
    public int? MaximumRedemptions { get; set; }

    /// <summary> Длительность демо-периода промокода в днях. </summary>
    public int? DemoDurationDays { get; set; }

    /// <summary> Код типа скидки промокода. </summary>
    public string DiscountTypeCode { get; set; } = PromoCodeCatalogCodes.DiscountTypes.None;

    /// <summary> Величина скидки промокода. </summary>
    public decimal? DiscountValue { get; set; }
    // Срок использования закреплённой денежной скидки (в днях с момента применения промокода).
    /// <summary> Срок действия скидки в днях. </summary>
    public int? DiscountValidityDays { get; set; }

    /// <summary> Ограничение количества скидочных платежей на пользователя. </summary>
    public int? DiscountPaymentLimitPerUser { get; set; }

    /// <summary> Признак применения промокода ко всем платным планам. </summary>
    public bool AppliesToAllPaidPlans { get; set; } = true;

    /// <summary> Количество бонусных дней по промокоду. </summary>
    public int? BonusDays { get; set; }

    /// <summary> Максимальное количество применений промокода организациями. </summary>
    public int? MaximumOrganizationApplications { get; set; }

    /// <summary> Признак применения промокода ко всем сценариям. </summary>
    public bool AppliesToAllScenarios { get; set; } = true;
    // Тариф-шаблон для немедленного подарочного доступа (существующий бизнес-тариф).
    /// <summary> Идентификатор шаблона подарочного тарифного плана. </summary>
    public int? GiftTemplatePlanId { get; set; }

    /// <summary> Шаблон тарифного плана для подарочной подписки. </summary>
    public SubscriptionPlan? GiftTemplatePlan { get; set; }
    // Маркер организационного промокода; старые записи остаются false (legacy-канал регистрации).
    /// <summary> Признак промокода для организаций. </summary>
    public bool IsOrganizationCode { get; set; } = false;

    /// <summary> Дата и время создания промокода. </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary> Дата и время последнего изменения промокода. </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary> Список тарифных планов, к которым применим промокод. </summary>
    public List<PromoCodePlan> Plans { get; set; } = [];

    /// <summary> Список активаций промокода пользователями. </summary>
    public List<PromoCodeRedemption> Redemptions { get; set; } = [];

    /// <summary> Список сценариев, к которым применим промокод. </summary>
    public List<PromoCodeScenario> Scenarios { get; set; } = [];

    /// <summary> Список применений промокода организациями. </summary>
    public List<PromoCodeApplication> Applications { get; set; } = [];
}
