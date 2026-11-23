using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenCRM.Services.Interfaces.Media.Dto;

/// <summary> Сведения о загруженном медиафайле. </summary>
public class MediaInfoResponse
{
    /// <summary> Идентификатор медиафайла. </summary>
    public Guid MediaId { get; set; }

    /// <summary> Тип медиафайла. </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary> Дата и время создания медиафайла. </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary> Идентификатор связанного объекта. </summary>
    public Guid? ObjectId { get; set; }
}

