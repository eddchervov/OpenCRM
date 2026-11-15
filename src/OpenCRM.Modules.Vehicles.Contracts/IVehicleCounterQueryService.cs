using OpenCRM.Modules.Vehicles.Contracts.Dtos;

namespace OpenCRM.Modules.Vehicles.Contracts;

/// <summary> Сервис чтения показаний счетчиков техники. </summary>
public interface IVehicleCounterQueryService
{
    /// <summary> Возвращает технику со счетчиками. </summary>
    Task<IReadOnlyList<VehicleCounterVehicleDto>> GetCounterVehiclesAsync(string userId, Guid? organizationId);

    /// <summary> Возвращает историю показаний счетчиков. </summary>
    Task<IReadOnlyList<VehicleCounterEntryDto>> GetCounterHistoryAsync(string userId, Guid? organizationId, Guid? vehicleId = null, int take = 100);

    /// <summary> Возвращает хронологию показаний счетчиков техники. </summary>
    Task<IReadOnlyList<VehicleCounterEntryDto>> GetCounterTimelineAsync(string userId, Guid? organizationId, Guid vehicleId, int take = 120);

    /// <summary> Возвращает страницу техники со счетчиками. </summary>
    Task<VehicleCounterVehiclesResponse> GetCounterVehiclesPageAsync(
        string userId,
        Guid? organizationId,
        int page = 1,
        int pageSize = 12,
        string? searchQuery = null,
        Guid? vehicleId = null,
        Guid? organizationClientId = null,
        string? sort = null);

    /// <summary> Возвращает страницу истории показаний. </summary>
    Task<VehicleCounterHistoryResponse> GetCounterHistoryPageAsync(
        string userId,
        Guid? organizationId,
        int page = 1,
        int pageSize = 12,
        string? searchQuery = null,
        Guid? vehicleId = null,
        Guid? organizationClientId = null,
        string? sort = null);

    /// <summary> Возвращает показание счетчика техники на дату. </summary>
    Task<VehicleCounterLookupDto> GetCounterEntryForDateAsync(string userId, Guid? organizationId, Guid vehicleId, DateTime recordedAt);
}
