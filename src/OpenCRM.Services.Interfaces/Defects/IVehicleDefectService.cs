using OpenCRM.Services.Interfaces.Defects.Dtos;

namespace OpenCRM.Services.Interfaces.Defects;

/// <summary> Управление дефектами техники. </summary>
public interface IVehicleDefectService
{
    /// <summary> Возвращает страницу дефектов с фильтрами и сортировкой. </summary>
    Task<VehicleDefectListResponse> GetAsync(
        string userId,
        Guid organizationId,
        int page = 1,
        int pageSize = 12,
        string? searchQuery = null,
        string? statusCode = null,
        Guid? vehicleId = null,
        string? sort = null);

    /// <summary> Возвращает дефект техники по идентификатору. </summary>
    Task<VehicleDefectDto?> GetByIdAsync(string userId, Guid organizationId, Guid defectId);

    /// <summary> Возвращает историю изменений дефекта. </summary>
    Task<IReadOnlyList<VehicleDefectHistoryItemDto>> GetHistoryAsync(string userId, Guid organizationId, Guid defectId);

    /// <summary> Возвращает варианты обслуживаний для связи с дефектом. </summary>
    Task<IReadOnlyList<VehicleDefectMaintenanceOptionDto>> GetMaintenanceOptionsAsync(string userId, Guid organizationId, Guid defectId);

    /// <summary> Возвращает варианты дефектов для связи с обслуживанием. </summary>
    Task<IReadOnlyList<VehicleDefectOptionDto>> GetOptionsForMaintenanceAsync(string userId, Guid organizationId, Guid maintenanceId);

    /// <summary> Создаёт дефект техники и возвращает его идентификатор. </summary>
    Task<Guid> CreateAsync(string userId, Guid organizationId, CreateVehicleDefectDto dto);

    /// <summary> Обновляет данные дефекта техники. </summary>
    Task UpdateAsync(string userId, Guid organizationId, Guid defectId, UpdateVehicleDefectDto dto);

    /// <summary> Обновляет статус дефекта техники. </summary>
    Task UpdateStatusAsync(string userId, Guid organizationId, Guid defectId, string statusCode);

    /// <summary> Связывает обслуживание с дефектом. </summary>
    Task LinkMaintenanceAsync(string userId, Guid organizationId, Guid defectId, Guid maintenanceId);

    /// <summary> Отвязывает обслуживание от дефекта. </summary>
    Task UnlinkMaintenanceAsync(string userId, Guid organizationId, Guid defectId, Guid maintenanceId);

    /// <summary> Удаляет дефект техники. </summary>
    Task DeleteAsync(string userId, Guid organizationId, Guid defectId);
}
