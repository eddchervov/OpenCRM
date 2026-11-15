using OpenCRM.Domain.Identity;

namespace OpenCRM.Domain;

/// <summary> Сформированный документ обслуживания. </summary>
public class MaintenanceDocument
{
    /// <summary> Идентификатор документа. </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary> Идентификатор обслуживания. </summary>
    public Guid MaintenanceId { get; set; }

    /// <summary> Обслуживание. </summary>
    public Maintenance Maintenance { get; set; } = default!;

    /// <summary> Идентификатор техники. </summary>
    public Guid VehicleId { get; set; }

    /// <summary> Идентификатор организации. </summary>
    public Guid? OrganizationId { get; set; }

    /// <summary> Идентификатор владельца записи. </summary>
    public string UserId { get; set; } = default!;

    /// <summary> Владелец записи. </summary>
    public ApplicationUser User { get; set; } = default!;

    /// <summary> Идентификатор пользователя, сформировавшего документ. </summary>
    public string CreatedByUserId { get; set; } = default!;

    /// <summary> Отображаемое имя пользователя на момент формирования. </summary>
    public string? CreatedByDisplayName { get; set; }

    /// <summary> Тип документа. </summary>
    public string Kind { get; set; } = default!;

    /// <summary> Формат документа. </summary>
    public string Format { get; set; } = default!;

    /// <summary> Признак включения фотографий в документ. </summary>
    public bool IncludePhotos { get; set; }

    /// <summary> Заголовок документа. </summary>
    public string Title { get; set; } = default!;

    /// <summary> Имя файла для скачивания. </summary>
    public string FileName { get; set; } = default!;

    /// <summary> Путь файла в хранилище. </summary>
    public string StoragePath { get; set; } = default!;

    /// <summary> MIME-тип файла. </summary>
    public string ContentType { get; set; } = default!;

    /// <summary> Размер файла в байтах. </summary>
    public long Size { get; set; }

    /// <summary> Версия документа в рамках обслуживания, типа, формата и состава. </summary>
    public int Version { get; set; }

    /// <summary> Дата формирования. </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary> Признак мягкого удаления. </summary>
    public bool IsDeleted { get; set; }
}
