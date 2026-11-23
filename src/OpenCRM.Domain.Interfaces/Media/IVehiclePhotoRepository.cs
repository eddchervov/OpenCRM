namespace OpenCRM.Domain.Interfaces.Media;

/// <summary> Репозиторий фотографий техники. </summary>
public interface IVehiclePhotoRepository
{
    /// <summary> Находит фотографию техники по идентификатору. </summary>
    Task<VehiclePhoto?> GetByIdAsync(string? userId, Guid photoId, bool includeDeleted = false);

    /// <summary> Находит фотографию техники по идентификатору техники. </summary>
    Task<VehiclePhoto?> GetByVehicleIdAsync(string userId, Guid vehicleId, bool includeDeleted = false);

    /// <summary> Находит фотографию техники с учётом организации. </summary>
    Task<VehiclePhoto?> GetByVehicleIdAsync(string userId, Guid? organizationId, Guid vehicleId, bool includeDeleted = false);

    /// <summary> Получает галерею фотографий техники. </summary>
    Task<List<VehiclePhoto>> GetGalleryByVehicleIdAsync(string userId, Guid? organizationId, Guid vehicleId, bool includeDeleted = false);

    /// <summary> Получает фотографии нескольких единиц техники. </summary>
    Task<List<VehiclePhoto>> GetByVehicleIdsAsync(string userId, IEnumerable<Guid> vehicleIds, bool includeDeleted = false);

    /// <summary> Получает фотографии нескольких единиц техники с учётом организации. </summary>
    Task<List<VehiclePhoto>> GetByVehicleIdsAsync(string userId, Guid? organizationId, IEnumerable<Guid> vehicleIds, bool includeDeleted = false);

    /// <summary> Получает фотографии техники пользователя. </summary>
    Task<IEnumerable<VehiclePhoto>> GetByUserAsync(string userId, bool includeDeleted = false);

    /// <summary> Находит системную фотографию техники по идентификатору. </summary>
    Task<VehiclePhoto?> GetSystemPhotoByIdAsync(Guid photoId, bool includeDeleted = false);

    /// <summary> Получает системные фотографии техники. </summary>
    Task<IEnumerable<VehiclePhoto>> GetSystemPhotosAsync(bool includeDeleted = false);

    /// <summary> Добавляет фотографию техники. </summary>
    Task AddAsync(VehiclePhoto photo);

    /// <summary> Обновляет фотографию техники. </summary>
    Task UpdateAsync(VehiclePhoto photo);

    /// <summary> Удаляет фотографию техники по идентификатору. </summary>
    Task DeleteAsync(Guid photoId);

    /// <summary> Проверяет существование фотографии техники. </summary>
    Task<bool> ExistsAsync(string? userId, Guid photoId, bool includeDeleted = false);

    /// <summary> Проверяет принадлежность фотографии техники пользователю. </summary>
    Task<bool> IsPhotoBelongsToUserAsync(string? userId, Guid photoId);

    /// <summary> Проверяет наличие фотографии у техники. </summary>
    Task<bool> HasVehiclePhotoAsync(string? userId, Guid vehicleId, bool includeDeleted = false);
}



