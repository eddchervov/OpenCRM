namespace OpenCRM.Modules.Vehicles.Contracts.Photos;

/// <summary> Данные фотографии техники. </summary>
public sealed class VehiclePhotoDto
{
    /// <summary> Идентификатор фотографии. </summary>
    public Guid PhotoId { get; set; }

    /// <summary> Идентификатор техники. </summary>
    public Guid VehicleId { get; set; }

    /// <summary> Идентификатор альбома, если фотография в нём. </summary>
    public Guid? AlbumId { get; set; }

    /// <summary> Признак главного фото. </summary>
    public bool IsPrimary { get; set; }

    /// <summary> Исходное имя файла фотографии. </summary>
    public string? OriginalFileName { get; set; }

    /// <summary> Размер файла фотографии. </summary>
    public long Size { get; set; }

    /// <summary> Тип содержимого файла. </summary>
    public string? ContentType { get; set; }

    /// <summary> Время загрузки фотографии. </summary>
    public DateTime CreatedAt { get; set; }
}
