namespace OpenCRM.Services.Interfaces.Promotions.Dtos;

/// <summary> DTO промокода для административного интерфейса. </summary>
public class PromoCodeAdminDto
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
    public bool IsActive { get; set; }

    /// <summary> Дата начала действия, если задана. </summary>
    public DateTime? ValidFrom { get; set; }

    /// <summary> Дата окончания действия, если задана. </summary>
    public DateTime? ValidUntil { get; set; }

    /// <summary> Максимальное число использований, если задано. </summary>
    public int? MaximumRedemptions { get; set; }

    /// <summary> Количество использований промокода. </summary>
    public int RedemptionCount { get; set; }

    /// <summary> Оставшееся число использований, если ограничено. </summary>
    public int? RemainingRedemptions { get; set; }

    /// <summary> Длительность демо-доступа в днях, если задана. </summary>
    public int? DemoDurationDays { get; set; }

    /// <summary> Код типа скидки. </summary>
    public string DiscountTypeCode { get; set; } = string.Empty;

    /// <summary> Величина скидки, если задана. </summary>
    public decimal? DiscountValue { get; set; }

    /// <summary> Срок действия скидки в днях, если задан. </summary>
    public int? DiscountValidityDays { get; set; }

    /// <summary> Ограничение скидок на пользователя, если задано. </summary>
    public int? DiscountPaymentLimitPerUser { get; set; }

    /// <summary> Действует ли промокод на все платные тарифные планы. </summary>
    public bool AppliesToAllPaidPlans { get; set; }

    /// <summary> Идентификаторы тарифных планов промокода. </summary>
    public List<int> SubscriptionPlanIds { get; set; } = [];

    /// <summary> Названия тарифных планов промокода. </summary>
    public List<string> SubscriptionPlanNames { get; set; } = [];

    /// <summary> Количество бонусных дней, если задано. </summary>
    public int? BonusDays { get; set; }

    /// <summary> Ограничение применений на организацию, если задано. </summary>
    public int? MaximumOrganizationApplications { get; set; }

    /// <summary> Количество применений промокода организациями. </summary>
    public int OrganizationApplicationCount { get; set; }

    /// <summary> Оставшееся число применений организациями, если ограничено. </summary>
    public int? RemainingOrganizationApplications { get; set; }

    /// <summary> Действует ли промокод на все сценарии. </summary>
    public bool AppliesToAllScenarios { get; set; }

    /// <summary> Коды сценариев, на которые действует промокод. </summary>
    public List<string> ScenarioCodes { get; set; } = [];
    // Тариф-шаблон для немедленного подарочного доступа (существующий бизнес-тариф).
    /// <summary> Идентификатор тарифного плана подарка, если задан. </summary>
    public int? GiftTemplatePlanId { get; set; }
    // Маркер организационного промокода (true — применяется через раздел «Тарифы» организации).
    /// <summary> Признак кода для организаций. </summary>
    public bool IsOrganizationCode { get; set; }

    /// <summary> Время создания промокода. </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary> Время последнего обновления промокода. </summary>
    public DateTime UpdatedAt { get; set; }
}
