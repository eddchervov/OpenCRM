using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenCRM.Services.Interfaces.FileStorages.Dtos;

/// <summary> Сведения о файле в файловом хранилище. </summary>
public class FileInfoDto
{
    /// <summary> Путь к файлу в хранилище. </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary> Имя файла. </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary> Размер файла в байтах. </summary>
    public long Size { get; set; }

    /// <summary> Тип содержимого файла. </summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary> Дата и время последнего изменения файла. </summary>
    public DateTime? LastModified { get; set; }

    /// <summary> Метаданные файла. </summary>
    public IDictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
}

