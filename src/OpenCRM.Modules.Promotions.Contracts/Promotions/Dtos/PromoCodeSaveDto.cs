using System.ComponentModel.DataAnnotations;

namespace OpenCRM.Services.Interfaces.Promotions.Dtos;

/// <summary> Данные для создания или изменения промокода. </summary>
public class PromoCodeSaveDto
{
    /// <summary> Код промокода. </summary>
    [Required]
    [StringLength(64)]
    public string Code { get; set; } = string.Empty;

    /// <summary> Название промокода. </summary>
    [Required]
    [StringLength(160)]
    public string Name { get; set; } = string.Empty;

    /// <summary> Описание промокода. </summary>
    [StringLength(1000)]
    public string? Description { get; set; }

    /// <summary> Признак активности промокода. </summary>
    public bool IsActive { get; set; } = true;

    /// <summary> Дата начала действия промокода. </summary>
    public DateTime? ValidFrom { get; set; }

    /// <summary> Дата окончания действия промокода. </summary>
    public DateTime? ValidUntil { get; set; }

    /// <summary> Максимальное количество погашений. </summary>
    [Range(1, int.MaxValue)]
    public int? MaximumRedemptions { get; set; }

    /// <summary> Длительность демо-периода в днях, если задана. </summary>
    [Range(1, 3650)]
    public int? DemoDurationDays { get; set; }

    /// <summary> Код типа скидки. </summary>
    [Required]
    public string DiscountTypeCode { get; set; } = "none";

    /// <summary> Величина скидки, если задана. </summary>
    [Range(typeof(decimal), "0.01", "100000000")]
    public decimal? DiscountValue { get; set; }

    /// <summary> Срок действия скидки в днях. </summary>
    [Range(1, 3650)]
    public int? DiscountValidityDays { get; set; }

    /// <summary> Ограничение суммы оплаты со скидкой на пользователя. </summary>
    [Range(1, int.MaxValue)]
    public int? DiscountPaymentLimitPerUser { get; set; }

    /// <summary> Признак применимости ко всем платным тарифам. </summary>
    public bool AppliesToAllPaidPlans { get; set; } = true;

    /// <summary> Идентификаторы тарифов, к которым применим промокод. </summary>
    public List<int> SubscriptionPlanIds { get; set; } = [];

    /// <summary> Количество бонусных дней. </summary>
    [Range(1, 3650)]
    public int? BonusDays { get; set; }

    /// <summary> Максимальное количество применений для организации. </summary>
    [Range(1, int.MaxValue)]
    public int? MaximumOrganizationApplications { get; set; }

    /// <summary> Признак применимости ко всем сценариям. </summary>
    public bool AppliesToAllScenarios { get; set; } = true;

    /// <summary> Коды сценариев, к которым применим промокод. </summary>
    public List<string> ScenarioCodes { get; set; } = [];

    // Тариф-шаблон для немедленного подарочного доступа (существующий бизнес-тариф).
    /// <summary> Идентификатор тарифа подарочного шаблона. </summary>
    public int? GiftTemplatePlanId { get; set; }
}
