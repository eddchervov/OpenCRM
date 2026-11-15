namespace OpenCRM.Modules.Vehicles.Contracts.Photos;

/// <summary> Данные фотоальбома техники. </summary>
public sealed class VehiclePhotoAlbumDto
{
    /// <summary> Идентификатор альбома. </summary>
    public Guid AlbumId { get; set; }

    /// <summary> Идентификатор техники. </summary>
    public Guid VehicleId { get; set; }

    /// <summary> Название альбома. </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary> Время создания альбома. </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary> Количество фотографий в альбоме. </summary>
    public int PhotoCount { get; set; }

    /// <summary> Идентификатор главного фото альбома. </summary>
    public Guid? PrimaryPhotoId { get; set; }

    /// <summary> Фотографии альбома. </summary>
    public IReadOnlyList<VehiclePhotoDto> Photos { get; set; } = [];
}
