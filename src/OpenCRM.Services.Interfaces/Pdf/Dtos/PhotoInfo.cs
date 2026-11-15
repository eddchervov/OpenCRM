using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenCRM.Services.Interfaces.Pdf.Dtos;

/// <summary> DTO фотографии для PDF-документов. </summary>
public class PhotoInfo
{
    /// <summary> Подпись к фотографии, если есть. </summary>
    public string? Caption { get; set; }

    /// <summary> Идентификатор исходной фотографии для сопоставления с осмотром. </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Guid? PhotoId { get; set; }

    /// <summary> Данные изображения фотографии. </summary>
    public byte[] ImageData { get; set; }
}

