using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenCRM.Domain;

namespace OpenCRM.Services.Interfaces.Subscriptions.Dtos.Admin
{
    /// <summary> DTO обновления тарифного плана. </summary>
    public class SubscriptionPlanUpdateDto
    {
        /// <summary> Название тарифного плана. </summary>
        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        /// <summary> Описание тарифного плана. </summary>
        [StringLength(1000)]
        public string Description { get; set; } = string.Empty;

        /// <summary> Цена тарифного плана. </summary>
        [Required]
        [Range(0, double.MaxValue)]
        public decimal Price { get; set; }

        /// <summary> Цена со скидкой, если есть. </summary>
        [Range(0, double.MaxValue)]
        public decimal? DiscountPrice { get; set; }

        /// <summary> Длительность тарифа в днях. </summary>
        [Required]
        [Range(1, int.MaxValue)]
        public int DurationDays { get; set; }

        /// <summary> Лимит транспортных средств тарифа. </summary>
        [Required]
        [Range(0, int.MaxValue)]
        public int VehicleLimit { get; set; }

        /// <summary> Порядок сортировки тарифа. </summary>
        public int Order { get; set; }

        /// <summary> Группа тарифа, если есть. </summary>
        [StringLength(100)]
        public string? Group { get; set; }

        /// <summary> Код типа тарифа. </summary>
        [Required]
        [StringLength(32)]
        public string PlanTypeCode { get; set; } = SubscriptionPlanCatalogCodes.PlanTypes.Personal;

        /// <summary> Признак активности тарифа. </summary>
        public bool IsActive { get; set; }

        /// <summary> Признак тарифа по умолчанию. </summary>
        public bool IsDefaultPlan { get; set; }

        /// <summary> Признак демо-тарифа. </summary>
        public bool IsDemoPlan { get; set; }
    }
}

