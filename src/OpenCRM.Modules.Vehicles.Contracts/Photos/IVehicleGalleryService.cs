namespace OpenCRM.Modules.Vehicles.Contracts.Photos;

/// <summary> Галерея фотографий техники. </summary>
public interface IVehicleGalleryService
{
    /// <summary> Загружает фотографию техники. </summary>
    Task<Guid> UploadVehiclePhotoAsync(string userId, Guid vehicleId, Stream imageStream, string fileName);

    /// <summary> Загружает фотографию техники в контексте организации. </summary>
    Task<Guid> UploadVehiclePhotoAsync(
        string userId,
        Guid? organizationId,
        Guid vehicleId,
        Stream imageStream,
        string fileName);

    /// <summary> Загружает несколько фотографий техники. </summary>
    Task<IReadOnlyList<Guid>> UploadVehiclePhotosAsync(
        string userId,
        Guid? organizationId,
        Guid vehicleId,
        IEnumerable<(Stream Stream, string FileName)> files);

    /// <summary> Возвращает фотографии техники. </summary>
    Task<IReadOnlyList<VehiclePhotoDto>> GetVehiclePhotosAsync(string userId, Guid? organizationId, Guid vehicleId);

    /// <summary> Возвращает фотографии нескольких единиц техники. </summary>
    Task<IReadOnlyList<VehiclePhotoDto>> GetVehiclePhotosAsync(
        string userId,
        Guid? organizationId,
        IEnumerable<Guid> vehicleIds);

    /// <summary> Удаляет фотографию техники. </summary>
    Task DeleteVehiclePhotoAsync(string userId, Guid vehicleId);

    /// <summary> Удаляет фотографию техники в контексте организации. </summary>
    Task DeleteVehiclePhotoAsync(string userId, Guid? organizationId, Guid vehicleId);

    /// <summary> Удаляет указанную фотографию из галереи. </summary>
    Task DeleteVehiclePhotoAsync(string userId, Guid? organizationId, Guid vehicleId, Guid photoId);

    /// <summary> Назначает главное фото техники. </summary>
    Task SetPrimaryVehiclePhotoAsync(string userId, Guid? organizationId, Guid vehicleId, Guid photoId);

    /// <summary> Возвращает путь к главному фото техники. </summary>
    Task<string?> GetVehiclePhotoPathAsync(string userId, Guid vehicleId);
}
