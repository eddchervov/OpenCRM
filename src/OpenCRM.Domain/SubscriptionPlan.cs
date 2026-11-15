using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenCRM.Domain
{
    /// <summary> Тарифный план подписки. </summary>
    public class SubscriptionPlan
    {
        /// <summary> Идентификатор тарифного плана. </summary>
        public int Id { get; set; }

        /// <summary> Название тарифного плана. </summary>
        public string Name { get; set; } = string.Empty;        // "Базовый", "Премиум", "Про"

        /// <summary> Описание тарифного плана. </summary>
        public string Description { get; set; } = string.Empty; // Описание преимуществ

        /// <summary> Цена тарифного плана. </summary>
        public decimal Price { get; set; }                      // Цена в месяц/период

        /// <summary> Цена со скидкой. </summary>
        public decimal? DiscountPrice { get; set; }             // Итоговая цена по скидке, если она ниже основной цены

        /// <summary> Длительность тарифного плана в днях. </summary>
        public int DurationDays { get; set; }                   // Длительность подписки в днях

        /// <summary> Признак активности тарифного плана. </summary>
        public bool IsActive { get; set; } = true;              // Активен ли план для покупки

        /// <summary> Признак наличия скидки у плана. </summary>
        public bool HasDiscount => DiscountPrice.HasValue && DiscountPrice.Value >= 0 && DiscountPrice.Value < Price;

        /// <summary> Итоговая цена плана с учётом скидки. </summary>
        public decimal EffectivePrice => HasDiscount ? DiscountPrice!.Value : Price;

        //public bool CanAddPremiumPhotos { get; set; }         // Можно загружать фото высокого качества
        /// <summary> Ограничение количества транспортных средств. </summary>
        public int VehicleLimit { get; set; }                       // Максимум техники на пользователя

        /// <summary> Порядок отображения тарифного плана. </summary>
        public int Order { get; set; }

        /// <summary> Группа тарифного плана. </summary>
        public string? Group { get; set; }

        /// <summary> Код типа тарифного плана подписки. </summary>
        public string PlanTypeCode { get; set; } = SubscriptionPlanCatalogCodes.PlanTypes.Personal;

        /// <summary> Признак демонстрационного тарифного плана. </summary>
        public bool IsDemoPlan { get; set; }

        /// <summary> Признак тарифного плана по умолчанию. </summary>
        public bool IsDefaultPlan { get; set; }

        /// <summary> Список возможностей, входящих в тарифный план. </summary>
        public List<SubscriptionPlanFeature> Features { get; set; } = [];
    }
}

