using OpenCRM.Services.Interfaces.Documents.Dtos;
using OpenCRM.Services.Interfaces.Documents;

namespace OpenCRM.Services.Interfaces.Word;

/// <summary> Контракт экспорта данных обслуживания и транспортных средств в документы Word. </summary>
public interface IWordDocumentExportService
{
    /// <summary> Формирует документ Word по транспортному средству для пользователя. </summary>
    Task<DocumentExportResult> GenerateVehicleWord(string userId, Guid vehicleId, bool includePhotos = false, Guid? organizationId = null);

    /// <summary> Формирует документ Word по записи об обслуживании. </summary>
    Task<DocumentExportResult> GenerateMaintenanceWord(string userId, Guid maintenanceId, bool includePhotos = false, Guid? organizationId = null);

    /// <summary> Формирует документ Word с актом работ по обслуживанию. </summary>
    Task<DocumentExportResult> GenerateMaintenanceWorkActWord(string userId, Guid maintenanceId, Guid organizationId);

    /// <summary> Формирует документ Word с ордером на работы по обслуживанию. </summary>
    Task<DocumentExportResult> GenerateMaintenanceWorkOrderWord(string userId, Guid maintenanceId, Guid organizationId);

    /// <summary> Формирует документ Word по обслуживанию на основе шаблона сценария. </summary>
    Task<DocumentExportResult> GenerateScenarioMaintenanceDocumentWord(string userId, Guid maintenanceId, Guid templateId, Guid organizationId);
}
