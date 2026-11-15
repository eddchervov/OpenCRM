using OpenCRM.Modules.Vehicles.Contracts.Dtos;

namespace OpenCRM.Modules.Vehicles.Contracts;

/// <summary> Контракт сервиса учёта сгенерированных документов. </summary>
public interface IVehicleDocumentHistoryService
{
    /// <summary> Фиксирует факт генерации документа по технике. </summary>
    Task TrackDocumentGenerated(string userId, Guid? organizationId, Guid vehicleId, string title, string? description = null);
}
