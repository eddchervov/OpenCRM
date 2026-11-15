namespace OpenCRM.Modules.Vehicles.Contracts.Photos;

/// <summary> Альбомы фотографий техники и ограничения тарифа. </summary>
public interface IVehiclePhotoAlbumService
{
    /// <summary> Возвращает фотоальбомы техники. </summary>
    Task<IReadOnlyList<VehiclePhotoAlbumDto>> GetVehiclePhotoAlbumsAsync(
        string userId,
        Guid? organizationId,
        Guid vehicleId);

    /// <summary> Возвращает фотоальбом техники. </summary>
    Task<VehiclePhotoAlbumDto> GetVehiclePhotoAlbumAsync(
        string userId,
        Guid? organizationId,
        Guid vehicleId,
        Guid albumId);

    /// <summary> Возвращает лимит фотоальбомов техники. </summary>
    Task<VehiclePhotoAlbumLimitDto> GetVehiclePhotoAlbumLimitAsync(
        string userId,
        Guid? organizationId,
        Guid vehicleId);

    /// <summary> Создаёт фотоальбом техники. </summary>
    Task<VehiclePhotoAlbumDto> CreateVehiclePhotoAlbumAsync(
        string userId,
        Guid? organizationId,
        Guid vehicleId,
        string title);

    /// <summary> Удаляет фотоальбом техники. </summary>
    Task DeleteVehiclePhotoAlbumAsync(string userId, Guid? organizationId, Guid vehicleId, Guid albumId);

    /// <summary> Загружает фотографии в альбом. </summary>
    Task<IReadOnlyList<Guid>> UploadVehicleAlbumPhotosAsync(
        string userId,
        Guid? organizationId,
        Guid vehicleId,
        Guid albumId,
        IEnumerable<(Stream Stream, string FileName)> files);

    /// <summary> Удаляет фотографию из альбома. </summary>
    Task DeleteVehicleAlbumPhotoAsync(string userId, Guid? organizationId, Guid vehicleId, Guid albumId, Guid photoId);

    /// <summary> Назначает главное фото альбома. </summary>
    Task SetPrimaryVehicleAlbumPhotoAsync(
        string userId,
        Guid? organizationId,
        Guid vehicleId,
        Guid albumId,
        Guid photoId);
}
