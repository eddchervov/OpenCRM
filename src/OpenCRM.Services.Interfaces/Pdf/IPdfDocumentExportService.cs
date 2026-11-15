using OpenCRM.Services.Interfaces.Documents.Dtos;
using OpenCRM.Services.Interfaces.Documents;

namespace OpenCRM.Services.Interfaces.Pdf;

/// <summary> Контракт сервиса экспорта PDF-документов. </summary>
public interface IPdfDocumentExportService
{
    /// <summary> Формирует PDF-отчёт по автомобилю. </summary>
    Task<DocumentExportResult> GenerateVehiclePdf(string userId, Guid vehicleId, bool includePhotos = false, Guid? organizationId = null);

    /// <summary> Формирует PDF-отчёт по обслуживанию. </summary>
    Task<DocumentExportResult> GenerateMaintenancePdf(string userId, Guid maintenanceId, bool includePhotos = false, Guid? organizationId = null);

    /// <summary> Формирует печатный акт приёмки автомобиля. </summary>
    Task<DocumentExportResult> PrintMaintenanceIntake(string userId, Guid maintenanceId, Guid organizationId);

    /// <summary> Формирует PDF акта выполненных работ. </summary>
    Task<DocumentExportResult> GenerateMaintenanceWorkActPdf(string userId, Guid maintenanceId, Guid organizationId);

    /// <summary> Формирует PDF наряда на работы. </summary>
    Task<DocumentExportResult> GenerateMaintenanceWorkOrderPdf(string userId, Guid maintenanceId, Guid organizationId);

    /// <summary> Формирует PDF документа по шаблону сценария. </summary>
    Task<DocumentExportResult> GenerateScenarioMaintenanceDocumentPdf(string userId, Guid maintenanceId, Guid templateId, Guid organizationId);
}
