namespace OpenCRM.Domain;

/// <summary> Альбом фотографий техники. </summary>
public class VehiclePhotoAlbum
{
    /// <summary> Идентификатор альбома. </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary> Идентификатор техники. </summary>
    public Guid VehicleId { get; set; }

    /// <summary> Идентификатор владельца. </summary>
    public string UserId { get; set; } = default!;

    /// <summary> Название альбома. </summary>
    public string Title { get; set; } = default!;

    /// <summary> Дата создания. </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary> Признак мягкого удаления. </summary>
    public bool IsDeleted { get; set; }

    /// <summary> Техника альбома. </summary>
    public virtual Vehicle Vehicle { get; set; } = default!;

    /// <summary> Фотографии альбома. </summary>
    public virtual ICollection<VehiclePhoto> Photos { get; set; } = new HashSet<VehiclePhoto>();
}
