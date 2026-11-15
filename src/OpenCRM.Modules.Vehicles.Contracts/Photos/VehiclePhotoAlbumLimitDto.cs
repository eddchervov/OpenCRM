namespace OpenCRM.Modules.Vehicles.Contracts.Photos;

/// <summary> Информация о лимите фотоальбомов. </summary>
public sealed class VehiclePhotoAlbumLimitDto
{
    /// <summary> Текущее количество альбомов. </summary>
    public int CurrentCount { get; set; }

    /// <summary> Максимальное количество альбомов. </summary>
    public int MaxCount { get; set; }

    /// <summary> Признак ограничения тарифом. </summary>
    public bool IsLimitedByPlan { get; set; }

    /// <summary> Признак возможности создания нового альбома. </summary>
    public bool CanCreateMore => CurrentCount < MaxCount;
}
