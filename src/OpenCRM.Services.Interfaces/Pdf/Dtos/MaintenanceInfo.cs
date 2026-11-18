using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenCRM.Services.Interfaces.Pdf.Dtos;

/// <summary> DTO данных обслуживания для PDF-документов. </summary>
public class MaintenanceInfo
{
    /// <summary> Тип обслуживания. </summary>
    public MaintenanceType Type { get; set; }

    /// <summary> Дата обслуживания в текстовом виде. </summary>
    public string Date { get; set; }

    /// <summary> Место проведения обслуживания. </summary>
    public string Location { get; set; }

    /// <summary> Исполнитель обслуживания. </summary>
    public string Performer { get; set; }

    /// <summary> Пробег на момент обслуживания, если известен. </summary>
    public int? Mileage { get; set; }

    /// <summary> Заметки приёмки, если есть. </summary>
    public string? IntakeNotes { get; set; }

    /// <summary> Комментарий к обслуживанию. </summary>
    public string Comment { get; set; }

    /// <summary> Стоимость обслуживания, если указана. </summary>
    public decimal? Price { get; set; }

    /// <summary> Код валюты денежных сумм обслуживания. </summary>
    public string CurrencyCode { get; set; } = "RUB";

    /// <summary> Строки работ и услуг обслуживания. </summary>
    public List<MaintenanceDetail> MaintenanceDetails { get; set; }

    /// <summary> Фотографии, относящиеся к обслуживанию. </summary>
    public IEnumerable<PhotoInfo> Photos { get; set; } 

    /// <summary> Количество фотографий обслуживания. </summary>
    public int PhotosCount => Photos?.Count() ?? 0; 
}
