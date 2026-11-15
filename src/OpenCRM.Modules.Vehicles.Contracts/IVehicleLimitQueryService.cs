using OpenCRM.Modules.Vehicles.Contracts.Dtos;

namespace OpenCRM.Modules.Vehicles.Contracts;

/// <summary> Контракт сервиса информации о лимите техники. </summary>
public interface IVehicleLimitQueryService
{
    /// <summary> Возвращает информацию о лимите техники пользователя. </summary>
    Task<VehicleLimitInfoDto> GetVehicleLimitInfoAsync(string userId);

    /// <summary> Возвращает информацию о лимите техники для организации. </summary>
    Task<VehicleLimitInfoDto> GetVehicleLimitInfoAsync(string userId, Guid? organizationId);
}
