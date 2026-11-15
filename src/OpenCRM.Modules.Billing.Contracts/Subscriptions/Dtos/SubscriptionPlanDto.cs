using OpenCRM.Services.Interfaces.Features.Dtos;

namespace OpenCRM.Services.Interfaces.Subscriptions.Dtos;

using OpenCRM.Domain;

/// <summary> Тарифный план подписки. </summary>
public class SubscriptionPlanDto
{
    /// <summary> Идентификатор тарифного плана. </summary>
    public int Id { get; set; }

    /// <summary> Название тарифного плана. </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary> Описание тарифного плана. </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary> Цена тарифного плана. </summary>
    public decimal Price { get; set; }

    /// <summary> Цена со скидкой. </summary>
    public decimal? DiscountPrice { get; set; }

    /// <summary> Первоначальная итоговая цена тарифа. </summary>
    public decimal OriginalEffectivePrice { get; set; }

    /// <summary> Признак промо-скидки. </summary>
    public bool HasPromoDiscount { get; set; }

    /// <summary> Промокод скидки. </summary>
    public string? PromoCode { get; set; }

    /// <summary> Итоговая цена тарифа. </summary>
    public decimal EffectivePrice { get; set; }

    /// <summary> Признак наличия скидки. </summary>
    public bool HasDiscount { get; set; }

    /// <summary> Длительность тарифа в днях. </summary>
    public int DurationDays { get; set; }

    /// <summary> Признак активного тарифа. </summary>
    public bool IsActive { get; set; } = true;

    /// <summary> Лимит транспортных средств тарифа. </summary>
    public int VehicleLimit { get; set; }

    /// <summary> Порядок отображения тарифа. </summary>
    public int Order { get; set; }

    /// <summary> Группа тарифа. </summary>
    public string? Group { get; set; }

    /// <summary> Код типа тарифа. </summary>
    public string PlanTypeCode { get; set; } = SubscriptionPlanCatalogCodes.PlanTypes.Personal;

    /// <summary> Название типа тарифа. </summary>
    public string PlanTypeName { get; set; } = "Личный";

    /// <summary> Признак демо-тарифа. </summary>
    public bool IsDemoPlan { get; set; }

    /// <summary> Признак тарифа по умолчанию. </summary>
    public bool IsDefaultPlan { get; set; }

    /// <summary> Список функций тарифа. </summary>
    public List<FeatureValueDto> Features { get; set; } = [];
}

