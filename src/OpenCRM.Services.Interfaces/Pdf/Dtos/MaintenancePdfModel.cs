using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenCRM.Services.Interfaces.Pdf.Dtos;

/// <summary> Модель PDF отчёта по обслуживанию. </summary>
public class MaintenancePdfModel
{
    /// <summary> Данные транспортного средства в отчёте. </summary>
    public VehicleInfo Vehicle { get; set; }

    /// <summary> Данные обслуживания в отчёте. </summary>
    public MaintenanceInfo Maintenance { get; set; }

    /// <summary> Дата формирования отчёта. </summary>
    public string GeneratedDate { get; set; }

    /// <summary> Информация о компании в отчёте. </summary>
    public string CompanyInfo { get; set; }
}

