namespace OpenCRM.Domain;

/// <summary> Фотография техники. </summary>
public class VehiclePhoto : BaseMedia
{
    /// <summary> Идентификатор техники. </summary>
    public Guid VehicleId { get; set; }

    /// <summary> Идентификатор альбома фотографии. Null означает общую галерею техники. </summary>
    public Guid? AlbumId { get; set; }

    /// <summary> Признак главной фотографии техники или альбома. </summary>
    public bool IsPrimary { get; set; }

    /// <summary> Техника фотографии. </summary>
    public virtual Vehicle Vehicle { get; set; } = default!;

    /// <summary> Альбом фотографии. </summary>
    public virtual VehiclePhotoAlbum? Album { get; set; }
}
