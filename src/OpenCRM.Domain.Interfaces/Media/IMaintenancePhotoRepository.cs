namespace OpenCRM.Domain.Interfaces.Media;

/// <summary> Репозиторий фотографий обслуживания. </summary>
public interface IMaintenancePhotoRepository
{
    /// <summary> Находит фотографию обслуживания по идентификатору. </summary>
    Task<MaintenancePhoto?> GetByIdAsync(string userId, Guid photoId, bool includeDeleted = false);

    /// <summary> Находит фотографию обслуживания с учётом организации. </summary>
    Task<MaintenancePhoto?> GetByIdAsync(string userId, Guid? organizationId, Guid photoId, bool includeDeleted = false);

    /// <summary> Получает фотографии обслуживания по идентификатору обслуживания. </summary>
    Task<IEnumerable<MaintenancePhoto>> GetByMaintenanceIdAsync(string userId, Guid maintenanceId, bool includeDeleted = false);

    /// <summary> Получает фотографии обслуживания с учётом организации. </summary>
    Task<IEnumerable<MaintenancePhoto>> GetByMaintenanceIdAsync(string userId, Guid? organizationId, Guid maintenanceId, bool includeDeleted = false);

    /// <summary> Получает фотографии по нескольким обслуживаниям. </summary>
    Task<IEnumerable<MaintenancePhoto>> GetByMaintenanceIdsAsync(string userId, IEnumerable<Guid> maintenanceIds, bool includeDeleted = false);

    /// <summary> Получает фотографии по нескольким обслуживаниям с учётом организации. </summary>
    Task<IEnumerable<MaintenancePhoto>> GetByMaintenanceIdsAsync(string userId, Guid? organizationId, IEnumerable<Guid> maintenanceIds, bool includeDeleted = false);

    /// <summary> Получает фотографии обслуживаний техники. </summary>
    Task<IEnumerable<MaintenancePhoto>> GetByVehicleIdAsync(string userId, Guid vehicleId, bool includeDeleted = false);

    /// <summary> Получает фотографии обслуживаний пользователя. </summary>
    Task<IEnumerable<MaintenancePhoto>> GetByUserAsync(string userId, bool includeDeleted = false);

    /// <summary> Добавляет фотографию обслуживания. </summary>
    Task AddAsync(MaintenancePhoto photo);

    /// <summary> Добавляет несколько фотографий обслуживания. </summary>
    Task AddRangeAsync(IEnumerable<MaintenancePhoto> photos);

    /// <summary> Обновляет фотографию обслуживания. </summary>
    Task UpdateAsync(MaintenancePhoto photo);

    /// <summary> Удаляет фотографию обслуживания по идентификатору. </summary>
    Task DeleteAsync(Guid photoId);

    /// <summary> Проверяет существование фотографии обслуживания. </summary>
    Task<bool> ExistsAsync(string userId, Guid photoId, bool includeDeleted = false);

    /// <summary> Проверяет принадлежность фотографии пользователю. </summary>
    Task<bool> IsPhotoBelongsToUserAsync(string userId, Guid photoId);

    /// <summary> Считает фотографии обслуживания. </summary>
    Task<int> GetCountByMaintenanceAsync(string userId, Guid maintenanceId, bool includeDeleted = false);
}


