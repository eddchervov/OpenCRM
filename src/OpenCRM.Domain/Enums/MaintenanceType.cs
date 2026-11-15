using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenCRM.Domain.Enums;

/// <summary> Тип обслуживания техники. </summary>
public enum MaintenanceType
{
    /// <summary> Плановое обслуживание техники. </summary>
    Maintenance = 1,
    /// <summary> Ремонт техники. </summary>
    Repair = 2,
    /// <summary> Заправка техники. </summary>
    Refill = 3,

    /// <summary> Прочий тип обслуживания. </summary>
    Other = 100
}

//Diagnostic = 90,             // Диагностика
//    CarWash = 91,                // Мойка автомобиля
//       TireService = 4,     // Шиномонтаж (замена, балансировка)
