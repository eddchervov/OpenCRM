using OpenCRM.Services.Interfaces.Documents.Dtos;
using OpenCRM.Modules.Documents;
using OpenCRM.Services.Interfaces.Documents;
using OpenCRM.Services.Interfaces.Pdf;

namespace OpenCRM.Modules.Documents.Pdf;

/// <summary> Сервис экспорта документов в PDF. </summary>
public class PdfDocumentExportService(
    VehicleReportWorkflow vehicleReportWorkflow,
    MaintenanceReportWorkflow maintenanceReportWorkflow,
    WorkDocumentsWorkflow workDocumentsWorkflow,
    ScenarioDocumentWorkflow scenarioDocumentWorkflow,
    MaintenanceIntakeWorkflow maintenanceIntakeWorkflow) : IPdfDocumentExportService
{
    /// <summary> Формирует PDF-отчёт по технике. </summary>
    public Task<DocumentExportResult> GenerateVehiclePdf(
        string userId,
        Guid vehicleId,
        bool includePhotos = false,
        Guid? organizationId = null) =>
        vehicleReportWorkflow.GenerateVehiclePdf(userId, vehicleId, includePhotos, organizationId);

    /// <summary> Формирует PDF-отчёт по обслуживанию. </summary>
    public Task<DocumentExportResult> GenerateMaintenancePdf(
        string userId,
        Guid maintenanceId,
        bool includePhotos = false,
        Guid? organizationId = null) =>
        maintenanceReportWorkflow.GenerateMaintenancePdf(userId, maintenanceId, includePhotos, organizationId);

    /// <summary> Печатает документ приёма обслуживания в PDF. </summary>
    public Task<DocumentExportResult> PrintMaintenanceIntake(string userId, Guid maintenanceId, Guid organizationId) =>
        maintenanceIntakeWorkflow.PrintMaintenanceIntake(userId, maintenanceId, organizationId);

    /// <summary> Формирует PDF-акт выполненных работ по обслуживанию. </summary>
    public Task<DocumentExportResult> GenerateMaintenanceWorkActPdf(string userId, Guid maintenanceId, Guid organizationId) =>
        workDocumentsWorkflow.GenerateMaintenanceWorkActPdf(userId, maintenanceId, organizationId);

    /// <summary> Формирует PDF-заказ-наряд по обслуживанию. </summary>
    public Task<DocumentExportResult> GenerateMaintenanceWorkOrderPdf(string userId, Guid maintenanceId, Guid organizationId) =>
        workDocumentsWorkflow.GenerateMaintenanceWorkOrderPdf(userId, maintenanceId, organizationId);

    /// <summary> Формирует PDF-документ сценария по обслуживанию. </summary>
    public Task<DocumentExportResult> GenerateScenarioMaintenanceDocumentPdf(
        string userId,
        Guid maintenanceId,
        Guid templateId,
        Guid organizationId) =>
        scenarioDocumentWorkflow.GenerateScenarioMaintenanceDocumentPdf(userId, maintenanceId, templateId, organizationId);
}
