using OpenCRM.Services.Interfaces.Features.Dtos;
using OpenCRM.Domain;

namespace OpenCRM.Services.Interfaces.Subscriptions.Dtos.Admin;

/// <summary> DTO тарифного плана для административного интерфейса. </summary>
public class SubscriptionPlanAdminDto
{
    /// <summary> Идентификатор тарифного плана. </summary>
    public int Id { get; set; }

    /// <summary> Название тарифного плана. </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary> Описание тарифного плана. </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary> Цена тарифного плана. </summary>
    public decimal Price { get; set; }

    /// <summary> Цена со скидкой, если есть. </summary>
    public decimal? DiscountPrice { get; set; }

    /// <summary> Итоговая цена с учётом скидки. </summary>
    public decimal EffectivePrice { get; set; }

    /// <summary> Признак наличия скидки. </summary>
    public bool HasDiscount { get; set; }

    /// <summary> Длительность тарифа в днях. </summary>
    public int DurationDays { get; set; }

    /// <summary> Признак активности тарифа. </summary>
    public bool IsActive { get; set; }

    /// <summary> Лимит транспортных средств тарифа. </summary>
    public int VehicleLimit { get; set; }

    /// <summary> Порядок сортировки тарифа. </summary>
    public int Order { get; set; }

    /// <summary> Группа тарифа, если есть. </summary>
    public string? Group { get; set; }

    /// <summary> Код типа тарифа. </summary>
    public string PlanTypeCode { get; set; } = SubscriptionPlanCatalogCodes.PlanTypes.Personal;

    /// <summary> Название типа тарифа. </summary>
    public string PlanTypeName { get; set; } = "Личный";

    /// <summary> Признак демо-тарифа. </summary>
    public bool IsDemoPlan { get; set; }

    /// <summary> Признак тарифа по умолчанию. </summary>
    public bool IsDefaultPlan { get; set; }

    /// <summary> Признак наличия истории использования. </summary>
    public bool HasUsageHistory { get; set; }

    /// <summary> Признак возможности удаления тарифа. </summary>
    public bool CanDelete { get; set; }

    /// <summary> Функции тарифного плана. </summary>
    public List<FeatureValueDto> Features { get; set; } = [];
}
