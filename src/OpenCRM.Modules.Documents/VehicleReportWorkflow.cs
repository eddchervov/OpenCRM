using OpenCRM.Services.Interfaces.Documents.Dtos;
using Helpers;
using OpenCRM.Services.Interfaces.Documents;
using OpenCRM.Services.Interfaces.Pdf;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Services.Interfaces.Word;

namespace OpenCRM.Modules.Documents;

/// <summary> Формирует экспортные отчёты по технике в PDF и Word. </summary>
public sealed class VehicleReportWorkflow(
    IPdfGeneratorService pdfGeneratorService,
    IDocxGeneratorService docxGeneratorService,
    IVehicleDocumentHistoryService vehicleService,
    IVehicleDocumentDataLoader vehicleDocumentDataLoader,
    DocumentExportAccess documentExportAccess,
    DocumentPhotoLoader documentPhotoLoader)
{
    /// <summary> Формирует PDF-отчёт по технике, при необходимости с фотографиями. </summary>
    public Task<DocumentExportResult> GenerateVehiclePdf(
        string userId,
        Guid vehicleId,
        bool includePhotos = false,
        Guid? organizationId = null) =>
        this.GenerateAsync(userId, vehicleId, includePhotos, organizationId, DocumentFormat.Pdf);

    /// <summary> Формирует Word-отчёт по технике, при необходимости с фотографиями. </summary>
    public Task<DocumentExportResult> GenerateVehicleWord(
        string userId,
        Guid vehicleId,
        bool includePhotos = false,
        Guid? organizationId = null) =>
        this.GenerateAsync(userId, vehicleId, includePhotos, organizationId, DocumentFormat.Word);

    /// <summary> Формирует отчёт по технике в заданном формате. </summary>
    private async Task<DocumentExportResult> GenerateAsync(
        string userId,
        Guid vehicleId,
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

        var data = await vehicleDocumentDataLoader.LoadAsync(userId, organizationId, vehicleId);
        var vehicle = data.Vehicle;
        if (vehicle == null)
        {
            return DocumentExportResult.NotFound("Техника не найдена");
        }

        var maintenanceHistory = data.MaintenanceHistory;
        if (maintenanceHistory.Count == 0)
        {
            return DocumentExportResult.InvalidRequest("Добавьте хотя бы одну запись обслуживания перед экспортом отчета.");
        }

        var vehiclePhoto = includePhotos && vehicle.PhotoId != null
            ? await documentPhotoLoader.GetPhotoDataAsync(userId, vehicle.PhotoId.Value)
            : null;
        byte[] documentBytes;
        string fileName;
        string contentType;

        if (format == DocumentFormat.Pdf)
        {
            var model = VehicleReportModelAdapter.CreatePdfHeader(vehicle, vehiclePhoto);
            foreach (var maintenance in maintenanceHistory)
            {
                model.MaintenanceHistory.Add(VehicleReportModelAdapter.CreatePdfMaintenance(maintenance));
            }

            model.GeneratedDate = DateTime.Now.ToString("dd.MM.yyyy HH:mm");
            model.TotalMaintenanceCount = maintenanceHistory.Count;
            model.TotalSpent = maintenanceHistory.Sum(maintenance => maintenance.Price ?? 0);
            documentBytes = await pdfGeneratorService.GenerateVehiclePdfAsync(model, includePhotos);
            contentType = "application/pdf";
            await vehicleService.TrackDocumentGenerated(
                userId,
                organizationId,
                vehicle.Id,
                "Сформирован отчет по технике PDF",
                MaintenanceDocumentFacts.BuildDocumentHistoryDescription("PDF", includePhotos));
            fileName = VehicleReportWorkflow.BuildFileName(vehicle, includePhotos, "pdf");
        }
        else
        {
            var model = VehicleReportModelAdapter.CreateWordHeader(vehicle, maintenanceHistory, vehiclePhoto);
            foreach (var maintenance in maintenanceHistory)
            {
                model.MaintenanceHistory.Add(VehicleReportModelAdapter.CreateWordMaintenance(maintenance));
            }

            documentBytes = await docxGeneratorService.GenerateVehicleHistoryDocxAsync(model, includePhotos);
            contentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
            await vehicleService.TrackDocumentGenerated(
                userId,
                organizationId,
                vehicle.Id,
                "Сформирован отчет по технике Word",
                MaintenanceDocumentFacts.BuildDocumentHistoryDescription("Word", includePhotos));
            fileName = VehicleReportWorkflow.BuildFileName(vehicle, includePhotos, "docx");
        }

        return DocumentExportResult.File(documentBytes, contentType, fileName);
    }

    /// <summary> Формирует имя файла отчёта по технике. </summary>
    private static string BuildFileName(
        OpenCRM.Modules.Vehicles.Contracts.Dtos.GetVehicleDto vehicle,
        bool includePhotos,
        string extension)
    {
        var vehicleName = MaintenanceDocumentFacts.SanitizeFileName(
            VehicleDisplayName.Format(vehicle.BrandName, vehicle.ModelName, vehicle.RegistrationPlate));
        var photoLabel = includePhotos ? "с_фото" : "без_фото";

        return $"Отчет_{vehicleName}_{photoLabel}_{DateTime.Now:yyyyMMdd}.{extension}";
    }

    /// <summary> Форматы экспорта отчёта по технике. </summary>
    private enum DocumentFormat
    {
        /// <summary> Экспорт в PDF. </summary>
        Pdf,

        /// <summary> Экспорт в Word. </summary>
        Word
    }
}
