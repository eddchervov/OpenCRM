using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenCRM.Services.Interfaces.Media.Dto;

/// <summary> Результат загрузки медиафайла. </summary>
public class MediaUploadResponse
{
    /// <summary> Идентификатор загруженного медиафайла. </summary>
    public Guid MediaId { get; set; }

    /// <summary> Сообщение о результате загрузки. </summary>
    public string Message { get; set; }
}

