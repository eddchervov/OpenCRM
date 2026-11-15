using OpenCRM.Services.Interfaces.Documents.Dtos;
using Helpers;
using OpenCRM.Services.Interfaces.Documents;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Services.Interfaces.Pdf;
using OpenCRM.Services.Interfaces.Word;

namespace OpenCRM.Modules.Documents;

/// <summary> Сценарий формирования отчётов по обслуживанию. </summary>
public sealed class MaintenanceReportWorkflow(
    IPdfGeneratorService pdfGeneratorService,
    IDocxGeneratorService docxGeneratorService,
    IMaintenanceDocumentService maintenanceDocumentService,
    IMaintenanceReportDataLoader maintenanceReportDataLoader,
    DocumentExportAccess documentExportAccess,
    DocumentPhotoLoader documentPhotoLoader,
    ProjectBrand? brand = null)
{
    /// <summary> Формирует PDF-отчёт по обслуживанию. </summary>
    public Task<DocumentExportResult> GenerateMaintenancePdf(
        string userId,
        Guid maintenanceId,
        bool includePhotos = false,
        Guid? organizationId = null) =>
        this.GenerateAsync(userId, maintenanceId, includePhotos, organizationId, DocumentFormat.Pdf);

    /// <summary> Формирует Word-отчёт по обслуживанию. </summary>
    public Task<DocumentExportResult> GenerateMaintenanceWord(
        string userId,
        Guid maintenanceId,
        bool includePhotos = false,
        Guid? organizationId = null) =>
        this.GenerateAsync(userId, maintenanceId, includePhotos, organizationId, DocumentFormat.Word);

    /// <summary> Формирует отчёт по обслуживанию в заданном формате. </summary>
    private async Task<DocumentExportResult> GenerateAsync(
        string userId,
        Guid maintenanceId,
        bool includePhotos,
        Guid? organizationId,
        DocumentFormat format)
    {
        if (organizationId.HasValue)
        {
            var workspaceAccess = await documentExportAccess.GetWorkspaceAccessAsync(userId, organizationId.Value);
            if (!workspaceAccess.ExportsEnabled)
            {
                return DocumentExportResult.AccessDenied(
                    workspaceAccess.ReadOnlyMessage ?? "Выгрузки для этого рабочего пространства временно недоступны.");
            }
        }

        var hasDocumentAccess = format == DocumentFormat.Pdf
            ? await documentExportAccess.HasPdfAccessAsync(userId, organizationId, includePhotos)
            : await documentExportAccess.HasWordAccessAsync(userId, organizationId, includePhotos);
        if (!hasDocumentAccess)
        {
            var formatName = format == DocumentFormat.Pdf ? "PDF" : "Word";
            var message = includePhotos
                ? $"У вас нет доступа к {formatName}-экспорту с фотографиями."
                : $"У вас нет доступа к {formatName}-экспорту.";
            return DocumentExportResult.AccessDenied(message);
        }

        var data = await maintenanceReportDataLoader.LoadAsync(userId, organizationId, maintenanceId);
        if (data == null)
        {
            return DocumentExportResult.NotFound("Обслуживание не найдено");
        }

        var vehicle = data.Vehicle;
        if (vehicle == null)
        {
            return DocumentExportResult.NotFound("Техника не найдена");
        }

        var maintenance = data.Maintenance;
        var vehiclePhoto = includePhotos && vehicle.PhotoId != null
            ? await documentPhotoLoader.GetPhotoDataAsync(userId, vehicle.PhotoId.Value)
            : null;
        byte[] documentBytes;
        string fileName;
        string contentType;

        if (format == DocumentFormat.Pdf)
        {
            var model = MaintenanceReportModelAdapter.CreatePdf(data, vehiclePhoto, brand?.DisplayName ?? ProjectBrand.DefaultName);
            model.Maintenance.Photos = includePhotos
                ? await documentPhotoLoader.LoadPdfMaintenancePhotosAsync(userId, organizationId, maintenance.Id)
                : [];
            model.GeneratedDate = DateTime.Now.ToString("dd.MM.yyyy HH:mm");
            fileName = MaintenanceReportWorkflow.BuildFileName(vehicle, maintenance.Date, includePhotos, "pdf");
            documentBytes = await pdfGeneratorService.GenerateMaintenancePdfAsync(model, includePhotos);
            contentType = "application/pdf";
        }
        else
        {
            var model = MaintenanceReportModelAdapter.CreateWord(data, vehiclePhoto, brand?.DisplayName ?? ProjectBrand.DefaultName);
            documentBytes = await docxGeneratorService.GenerateMaintenanceDocxAsync(model, includePhotos);
            fileName = MaintenanceReportWorkflow.BuildFileName(vehicle, maintenance.Date, includePhotos, "docx");
            contentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
        }

        var documentFormat = format == DocumentFormat.Pdf ? "PDF" : "Word";
        await maintenanceDocumentService.TrackDocumentGenerated(
            userId,
            organizationId,
            maintenance.Id,
            $"Сформирован отчет обслуживания {documentFormat}",
            MaintenanceDocumentFacts.BuildDocumentHistoryDescription(documentFormat, includePhotos));

        return DocumentExportResult.File(documentBytes, contentType, fileName);
    }

    /// <summary> Строит имя файла отчёта по обслуживанию. </summary>
    private static string BuildFileName(
        OpenCRM.Modules.Vehicles.Contracts.Dtos.GetVehicleDto vehicle,
        DateTime maintenanceDate,
        bool includePhotos,
        string extension)
    {
        var prefix = includePhotos ? "Отчет_обслуживание" : "Отчет_обслуживание_без_фото";
        var vehicleName = MaintenanceDocumentFacts.SanitizeFileName(
            VehicleDisplayName.Format(vehicle.BrandName, vehicle.ModelName, vehicle.RegistrationPlate));

        return $"{prefix}_{vehicleName}_{maintenanceDate:yyyyMMdd}.{extension}";
    }

    /// <summary> Формат экспорта отчёта по обслуживанию. </summary>
    private enum DocumentFormat
    {
        /// <summary> Формат PDF. </summary>
        Pdf,

        /// <summary> Формат Word. </summary>
        Word
    }
}
