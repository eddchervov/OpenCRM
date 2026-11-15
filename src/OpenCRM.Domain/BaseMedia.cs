using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenCRM.Domain;

/// <summary> Базовая сущность медиафайла с общими атрибутами. </summary>
public abstract class BaseMedia
{
    /// <summary> Идентификатор медиафайла (GUID). </summary>
    public Guid Id { get; set; }

    /// <summary> Путь к медиафайлу. </summary>
    public string Path { get; set; }

    /// <summary> Тип медиафайла. </summary>
    public MediaType Type { get; set; }

    /// <summary> Дата и время создания медиафайла. </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary> Признак пометки медиафайла как удалённого. </summary>
    public bool IsDeleted { get; set; }

    /// <summary> Размер медиафайла. </summary>
    public long Size { get; set; }

    /// <summary> MIME-тип содержимого медиафайла. </summary>
    public string? ContentType { get; set; }

    /// <summary> Исходное имя файла. </summary>
    public string? OriginalFileName { get; set; }

    // Навигационные свойства
    /// <summary> Идентификатор пользователя, загрузившего медиафайл. </summary>
    public string? UserId { get; set; }

    /// <summary> Пользователь, связанный с медиафайлом. </summary>
    public virtual ApplicationUser? User { get; set; }
}

