using OpenCRM.Modules.Vehicles.Contracts.Dtos;

namespace OpenCRM.Modules.Vehicles.Contracts;

/// <summary> Контракт сервиса чтения техники. </summary>
public interface IVehicleQueryService
{
    /// <summary> Возвращает технику по идентификатору. </summary>
    Task<GetVehicleDto> GetById(string userId, Guid vehicleId);

    /// <summary> Возвращает технику по идентификатору в контексте организации. </summary>
    Task<GetVehicleDto> GetById(string userId, Guid? organizationId, Guid vehicleId);

    /// <summary> Возвращает страницу списка техники пользователя. </summary>
    Task<GetVehiclesResponse> Get(string userId, int page, int pageSize, bool isArchived, string? brandFilter = null, int? kind = null, string? searchQuery = null, Guid? organizationClientId = null, bool canViewMaintenance = false);

    /// <summary> Возвращает страницу списка техники для организации. </summary>
    Task<GetVehiclesResponse> Get(string userId, Guid? organizationId, int page, int pageSize, bool isArchived, string? brandFilter = null, int? kind = null, string? searchQuery = null, Guid? organizationClientId = null, bool canViewMaintenance = false);

    /// <summary> Возвращает количество техники пользователя. </summary>
    Task<int> GetVehiclesCountAsync(string userId);

    /// <summary> Возвращает количество техники организации. </summary>
    Task<int> GetVehiclesCountAsync(string userId, Guid? organizationId);

    /// <summary> Возвращает количество архивной техники пользователя. </summary>
    Task<int> GetArchivedVehiclesCountAsync(string userId);

    /// <summary> Возвращает количество архивной техники организации. </summary>
    Task<int> GetArchivedVehiclesCountAsync(string userId, Guid? organizationId);

    /// <summary> Возвращает историю событий по технике. </summary>
    Task<IReadOnlyList<VehicleHistoryEventDto>> GetHistory(string userId, Guid vehicleId, int take = 100);

    /// <summary> Возвращает историю событий по технике в контексте организации. </summary>
    Task<IReadOnlyList<VehicleHistoryEventDto>> GetHistory(string userId, Guid? organizationId, Guid vehicleId, int take = 100);
}
