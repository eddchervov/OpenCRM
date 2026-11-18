using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenCRM.Services.Interfaces.Word.Dtos
{
    /// <summary> Модель для формирования отчёта по транспортному средству в Word. </summary>
    public class WordVehicleModel
    {
        /// <summary> Основные данные транспортного средства. </summary>
        public WordVehicleInfo Vehicle { get; set; }

        /// <summary> Список обслуживаний транспортного средства. </summary>
        public List<WordMaintenanceInfo> MaintenanceHistory { get; set; }

        /// <summary> Дата формирования отчёта в текстовом виде. </summary>
        public string GeneratedDate { get; set; }

        /// <summary> Общее количество обслуживаний. </summary>
        public int TotalMaintenanceCount { get; set; }

        /// <summary> Общая сумма расходов по обслуживанию. </summary>
        public decimal TotalSpent { get; set; }

        public IReadOnlyDictionary<string, decimal> CurrencyTotals { get; set; } = new Dictionary<string, decimal>();
    }
}


