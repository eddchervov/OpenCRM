using OpenCRM.Services.Interfaces.Documents.Dtos;
using OpenCRM.Modules.Documents;
using OpenCRM.Services.Interfaces.Documents;
using OpenCRM.Services.Interfaces.Word;

namespace OpenCRM.Modules.Documents.Word;

/// <summary> Единая точка экспорта Word-документов через профильные рабочие процессы. </summary>
public class WordDocumentExportService(
    VehicleReportWorkflow vehicleReportWorkflow,
    MaintenanceReportWorkflow maintenanceReportWorkflow,
    WorkDocumentsWorkflow workDocumentsWorkflow,
    ScenarioDocumentWorkflow scenarioDocumentWorkflow) : IWordDocumentExportService
{
    /// <summary> Формирует Word-отчёт по технике. </summary>
    public Task<DocumentExportResult> GenerateVehicleWord(
        string userId,
        Guid vehicleId,
        bool includePhotos = false,
        Guid? organizationId = null) =>
        vehicleReportWorkflow.GenerateVehicleWord(userId, vehicleId, includePhotos, organizationId);

    /// <summary> Формирует Word-отчёт по обслуживанию. </summary>
    public Task<DocumentExportResult> GenerateMaintenanceWord(
        string userId,
        Guid maintenanceId,
        bool includePhotos = false,
        Guid? organizationId = null) =>
        maintenanceReportWorkflow.GenerateMaintenanceWord(userId, maintenanceId, includePhotos, organizationId);

    /// <summary> Формирует Word-акт выполненных работ по обслуживанию. </summary>
    public Task<DocumentExportResult> GenerateMaintenanceWorkActWord(string userId, Guid maintenanceId, Guid organizationId) =>
        workDocumentsWorkflow.GenerateMaintenanceWorkActWord(userId, maintenanceId, organizationId);

    /// <summary> Формирует Word-заказ-наряд по обслуживанию. </summary>
    public Task<DocumentExportResult> GenerateMaintenanceWorkOrderWord(string userId, Guid maintenanceId, Guid organizationId) =>
        workDocumentsWorkflow.GenerateMaintenanceWorkOrderWord(userId, maintenanceId, organizationId);

    /// <summary> Формирует Word-документ сценария обслуживания. </summary>
    public Task<DocumentExportResult> GenerateScenarioMaintenanceDocumentWord(
        string userId,
        Guid maintenanceId,
        Guid templateId,
        Guid organizationId) =>
        scenarioDocumentWorkflow.GenerateScenarioMaintenanceDocumentWord(userId, maintenanceId, templateId, organizationId);
}
