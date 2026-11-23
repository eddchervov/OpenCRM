using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenCRM.Services.Interfaces.Pdf.Dtos
{
    /// <summary> Модель PDF отчёта по автомобилю. </summary>
    public class VehiclePdfModel
    {
        /// <summary> Данные автомобиля в отчёте. </summary>
        public required VehicleInfo Vehicle { get; set; }

        /// <summary> История обслуживаний автомобиля. </summary>
        public List<MaintenanceInfo> MaintenanceHistory { get; set; } = [];

        /// <summary> Дата формирования отчёта. </summary>
        public string GeneratedDate { get; set; } = string.Empty;

        /// <summary> Общее количество обслуживаний. </summary>
        public int TotalMaintenanceCount { get; set; }

        /// <summary> Суммарные затраты по обслуживанию. </summary>
        public decimal TotalSpent { get; set; }

        public IReadOnlyDictionary<string, decimal> CurrencyTotals { get; set; } = new Dictionary<string, decimal>();
    }
}


