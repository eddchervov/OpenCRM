using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenCRM.Services.Interfaces.Word.Dtos
{
    /// <summary> Модель Word-документа по обслуживанию. </summary>
    public class WordMaintenanceModel
    {
        /// <summary> Данные автомобиля в документе. </summary>
        public required WordVehicleInfo Vehicle { get; set; }

        /// <summary> Данные обслуживания в документе. </summary>
        public required WordMaintenanceInfo Maintenance { get; set; }

        /// <summary> Дата формирования документа. </summary>
        public string GeneratedDate { get; set; } = string.Empty;

        /// <summary> Информация о компании в документе. </summary>
        public string? CompanyInfo { get; set; }
    }

}

