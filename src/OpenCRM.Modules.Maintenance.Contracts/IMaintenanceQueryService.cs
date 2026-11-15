using OpenCRM.Modules.Maintenance.Contracts.Dtos;

namespace OpenCRM.Modules.Maintenance.Contracts;

/// <summary> Читает обслуживание, историю и снимки заказов. </summary>
public interface IMaintenanceQueryService
{
    /// <summary> Возвращает обслуживание по идентификатору. </summary>
    Task<GetMaintenanceByIdDto> GetById(string userId, Guid maintenanceId);

    /// <summary> Возвращает обслуживание по идентификатору в контексте организации. </summary>
    Task<GetMaintenanceByIdDto> GetById(string userId, Guid? organizationId, Guid maintenanceId);

    /// <summary> Возвращает страницу обслуживаний транспортного средства с фильтрами. </summary>
    Task<MaintenanceListResponse> Get(string userId, Guid vehicleId, int page = 1, int pageSize = 10, string? sort = null, int? type = null, int? status = null);

    /// <summary> Возвращает страницу обслуживаний в контексте организации с фильтрами. </summary>
    Task<MaintenanceListResponse> Get(string userId, Guid? organizationId, Guid vehicleId, int page = 1, int pageSize = 10, string? sort = null, int? type = null, int? status = null);

    /// <summary> Возвращает страницу удалённых обслуживаний транспортного средства. </summary>
    Task<MaintenanceListResponse> GetDeleted(string userId, Guid vehicleId, int page = 1, int pageSize = 10);

    /// <summary> Возвращает страницу удалённых обслуживаний в контексте организации. </summary>
    Task<MaintenanceListResponse> GetDeleted(string userId, Guid? organizationId, Guid vehicleId, int page = 1, int pageSize = 10);

    /// <summary> Возвращает все обслуживания транспортного средства. </summary>
    Task<IEnumerable<GetMaintenanceDto>> Get(string userId, Guid vehicleId);

    /// <summary> Возвращает все обслуживания транспортного средства в контексте организации. </summary>
    Task<IEnumerable<GetMaintenanceDto>> Get(string userId, Guid? organizationId, Guid vehicleId);

    /// <summary> Возвращает историю событий обслуживания. </summary>
    Task<IReadOnlyList<MaintenanceHistoryEventDto>> GetHistory(string userId, Guid maintenanceId);

    /// <summary> Возвращает историю событий обслуживания в контексте организации. </summary>
    Task<IReadOnlyList<MaintenanceHistoryEventDto>> GetHistory(string userId, Guid? organizationId, Guid maintenanceId);
}