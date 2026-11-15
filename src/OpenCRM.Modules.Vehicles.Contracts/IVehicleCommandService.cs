using OpenCRM.Modules.Vehicles.Contracts.Dtos;

namespace OpenCRM.Modules.Vehicles.Contracts;

/// <summary> Контракт сервиса изменения техники. </summary>
public interface IVehicleCommandService
{
    /// <summary> Создаёт технику пользователя. </summary>
    Task<Guid> Create(string userId, CreateVehicleDto dto);

    /// <summary> Создаёт технику для пользователя или организации. </summary>
    Task<Guid> Create(string userId, Guid? organizationId, CreateVehicleDto dto);

    /// <summary> Обновляет данные техники. </summary>
    Task Update(string userId, Guid vehicleId, UpdateVehicleDto dto);

    /// <summary> Обновляет данные техники в контексте организации. </summary>
    Task Update(string userId, Guid? organizationId, Guid vehicleId, UpdateVehicleDto dto);

    /// <summary> Привязывает клиента организации только к автомобилю без прежнего клиента. </summary>
    Task AssignClientIfUnlinkedAsync(string userId, Guid organizationId, Guid vehicleId, Guid clientId);

    /// <summary> Помещает технику в архив. </summary>
    Task Archive(string userId, Guid vehicleId);

    /// <summary> Помещает технику в архив в контексте организации. </summary>
    Task Archive(string userId, Guid? organizationId, Guid vehicleId);

    /// <summary> Возвращает технику из архива. </summary>
    Task RestoreFromArchive(string userId, Guid vehicleId);

    /// <summary> Возвращает технику из архива в контексте организации. </summary>
    Task RestoreFromArchive(string userId, Guid? organizationId, Guid vehicleId);

    /// <summary> Передаёт технику организации. </summary>
    Task TransferToOrganizationAsync(string userId, Guid vehicleId, Guid organizationId);
}
