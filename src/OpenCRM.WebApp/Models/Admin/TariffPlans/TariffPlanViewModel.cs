using System.ComponentModel.DataAnnotations;

namespace OpenCRM.WebApp.Models.Admin.TariffPlans
{
    /// <summary> Модель представления тарифного плана в панели администратора. </summary>
    public class TariffPlanViewModel
    {
        /// <summary> Идентификатор тарифного плана. </summary>
        public int Id { get; set; }

        /// <summary> Название тарифного плана. </summary>
        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        /// <summary> Описание тарифного плана. </summary>
        [StringLength(500)]
        public string Description { get; set; } = string.Empty;

        /// <summary> Цена тарифного плана. </summary>
        [Required]
        [Range(0, double.MaxValue)]
        public decimal Price { get; set; }

        /// <summary> Длительность действия плана в днях. </summary>
        [Required]
        public int DurationDays { get; set; }

        /// <summary> Признак активности тарифного плана. </summary>
        public bool IsActive { get; set; } = true;

        /// <summary> Дата создания плана. </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary> Дата последнего изменения плана. </summary>
        public DateTime? UpdatedAt { get; set; }
    }
}

