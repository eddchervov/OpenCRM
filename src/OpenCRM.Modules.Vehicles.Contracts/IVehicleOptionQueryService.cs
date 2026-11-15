using OpenCRM.Modules.Vehicles.Contracts.Dtos;

namespace OpenCRM.Modules.Vehicles.Contracts;

/// <summary> Читает варианты техники организации для селекторов рабочего пространства. </summary>
public interface IVehicleOptionQueryService
{
    /// <summary> Возвращает активную технику организации после проверки доступа вызывающим сценарием. </summary>
    Task<IReadOnlyList<VehicleOptionDto>> GetOptionsAsync(
        Guid organizationId,
        string? searchQuery,
        Guid? organizationClientId,
        int take);
}
