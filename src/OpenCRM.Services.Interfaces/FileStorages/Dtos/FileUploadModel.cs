using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenCRM.Services.Interfaces.FileStorages.Dtos
{
    /// <summary> Модель загрузки файла в хранилище. </summary>
    public class FileUploadModel
    {
        /// <summary> Содержимое загружаемого файла. </summary>
        public required Stream Content { get; set; } = null!;

        /// <summary> Имя загружаемого файла. </summary>
        public required string FileName { get; set; } = string.Empty;

        /// <summary> Тип содержимого загружаемого файла. </summary>
        public required string ContentType { get; set; } = string.Empty;

        /// <summary> Размер загружаемого файла в байтах. </summary>
        public required long Size { get; set; }
    }
}

