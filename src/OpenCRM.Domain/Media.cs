using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;

namespace OpenCRM.Domain;

/// <summary> Универсальная модель медиа. </summary>
public class Media
{
    /// <summary> Идентификатор медиа. </summary>
    public Guid Id { get; set; }

    /// <summary> Идентификатор пользователя. </summary>
    public string? UserId { get; set; }

    /// <summary> Путь к файлу. </summary>
    public string? Path { get; set; }

    /// <summary> Тип медиа. </summary>
    public MediaType Type { get; set; }

    /// <summary> Дата создания. </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary> Идентификатор связанного объекта. </summary>
    public Guid? ObjectId { get; set; }

    /// <summary> Признак удаления. </summary>
    public bool IsDeleted { get; set; }

    /// <summary> Размер файла. </summary>
    public long Size { get; set; }

    /// <summary> Content type файла. </summary>
    public string? ContentType { get; set; }

    /// <summary> Исходное имя файла. </summary>
    public string? OriginalFileName { get; set; }

    /// <summary> Навигация к технике. </summary>
    public virtual Vehicle? Vehicle { get; set; }

    /// <summary> Навигация к обслуживанию. </summary>
    public virtual Maintenance? Maintenance { get; set; }

    /// <summary> Навигация к пользователю. </summary>
    public virtual ApplicationUser? User { get; set; }
}
