using OpenCRM.Modules.Vehicles.Contracts.Dtos;

namespace OpenCRM.Modules.Vehicles.Contracts;

/// <summary> Изменяет показания счетчиков техники. </summary>
public interface IVehicleCounterCommandService
{
    /// <summary> Сохраняет показание счетчика техники. </summary>
    Task UpdateCounterAsync(string userId, Guid? organizationId, UpdateVehicleCounterDto dto);

    /// <summary> Удаляет запись показания счетчика. </summary>
    Task DeleteCounterEntryAsync(string userId, Guid? organizationId, Guid entryId);

    /// <summary> Синхронизирует счетчик с записью обслуживания. </summary>
    Task SyncCounterFromMaintenanceAsync(
        string userId,
        Guid? organizationId,
        Guid vehicleId,
        Guid maintenanceId,
        int? value,
        DateTime recordedAt);

    /// <summary> Убирает связь счетчика с записью обслуживания. </summary>
    Task RemoveCounterSyncFromMaintenanceAsync(
        string userId,
        Guid? organizationId,
        Guid vehicleId,
        Guid maintenanceId);
}
