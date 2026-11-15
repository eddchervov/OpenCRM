using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenCRM.Services.Interfaces.Pdf.Dtos;

/// <summary> DTO строки работы или услуги для PDF-документов. </summary>
public class MaintenanceDetail
{
    /// <summary> Количество по строке. </summary>
    public int Quantity { get; set; } = 1;

    /// <summary> Название работы или услуги. </summary>
    public string Name { get; set; }

    /// <summary> Описание работы или услуги. </summary>
    public string Description { get; set; }

    /// <summary> Цена за единицу, если указана. </summary>
    public decimal? Price { get; set; }
}
