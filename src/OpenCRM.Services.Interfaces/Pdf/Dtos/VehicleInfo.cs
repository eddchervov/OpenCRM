using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenCRM.Domain.Enums;

namespace OpenCRM.Services.Interfaces.Pdf.Dtos;

/// <summary> DTO данных автомобиля для PDF-документов. </summary>
public class VehicleInfo
{
    /// <summary> Марка автомобиля. </summary>
    public string BrandName { get; set; } = string.Empty;

    /// <summary> Модель автомобиля. </summary>
    public string ModelName { get; set; } = string.Empty;

    /// <summary> Регистрационный знак, если есть. </summary>
    public string? RegistrationPlate { get; set; }

    /// <summary> Единица измерения пробега автомобиля. </summary>
    public VehicleMileageUnit MileageUnit { get; set; } = VehicleMileageUnit.Kilometers;

    /// <summary> Год выпуска, если известен. </summary>
    public int? YearIssue { get; set; }

    /// <summary> Отображаемое название автомобиля. </summary>
    public string? Name { get; set; }

    /// <summary> Описание автомобиля. </summary>
    public string? Description { get; set; }

    /// <summary> Данные фотографии автомобиля. </summary>
    public byte[]? PhotoData { get; set; }
}

