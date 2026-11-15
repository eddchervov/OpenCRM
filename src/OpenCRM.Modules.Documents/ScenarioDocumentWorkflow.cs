using OpenCRM.Services.Interfaces.Documents.Dtos;
using OpenCRM.Modules.Catalog.Contracts;
using OpenCRM.Modules.Organizations.Contracts;
using Helpers;
using OpenCRM.Services.Interfaces.Documents;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Pdf;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Vehicles.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Word;
using OrganizationScenarioDocumentKindCodes = OpenCRM.Domain.OrganizationScenarioDocumentKindCodes;

namespace OpenCRM.Modules.Documents;

/// <summary> Формирует экспортные документы сценария обслуживания в PDF и Word. </summary>
public sealed class ScenarioDocumentWorkflow(
    IPdfGeneratorService pdfGeneratorService,
    IDocxGeneratorService docxGeneratorService,
    IMaintenanceQueryService maintenanceQueryService,
    IMaintenanceDocumentService maintenanceDocumentService,
    IVehicleQueryService vehicleService,
    IOrganizationQueryService organizationService,
    ICatalogQueryService organizationScenarioCatalogService,
    DocumentExportAccess documentExportAccess,
    DocumentPhotoLoader documentPhotoLoader)
{
    /// <summary> Формирует PDF-документ сценария обслуживания. </summary>
    public Task<DocumentExportResult> GenerateScenarioMaintenanceDocumentPdf(
        string userId,
        Guid maintenanceId,
        Guid templateId,
        Guid organizationId) =>
        this.GenerateAsync(userId, maintenanceId, templateId, organizationId, DocumentFormat.Pdf);

    /// <summary> Формирует Word-документ сценария обслуживания. </summary>
    public Task<DocumentExportResult> GenerateScenarioMaintenanceDocumentWord(
        string userId,
        Guid maintenanceId,
        Guid templateId,
        Guid organizationId) =>
        this.GenerateAsync(userId, maintenanceId, templateId, organizationId, DocumentFormat.Word);

    /// <summary> Формирует документ сценария в заданном формате. </summary>
    private async Task<DocumentExportResult> GenerateAsync(
        string userId,
        Guid maintenanceId,
        Guid templateId,
        Guid organizationId,
        DocumentFormat format)
    {
        var workspaceAccess = await documentExportAccess.GetWorkspaceAccessAsync(userId, organizationId);
        if (!workspaceAccess.ExportsEnabled)
        {
            return DocumentExportResult.AccessDenied(
                workspaceAccess.ReadOnlyMessage ?? "Выгрузки для этого рабочего пространства временно недоступны.");
        }

        var catalog = await organizationScenarioCatalogService.GetAsync(userId, organizationId);
        var template = catalog.Documents.FirstOrDefault(document => document.Id == templateId && document.IsActive);
        if (template == null)
        {
            return DocumentExportResult.NotFound("Шаблон документа не найден");
        }

        var documentKindCode = OrganizationScenarioDocumentKindCodes.Normalize(template.DocumentKindCode)
            ?? OrganizationScenarioDocumentKindCodes.MaintenanceReport;
        var includePhotos = MaintenanceDocumentFacts.ShouldIncludePhotos(documentKindCode);
        var hasDocumentAccess = format == DocumentFormat.Pdf
            ? await documentExportAccess.HasPdfAccessAsync(userId, organizationId, includePhotos)
            : await documentExportAccess.HasWordAccessAsync(userId, organizationId, includePhotos);
        if (!hasDocumentAccess)
        {
            var message = format == DocumentFormat.Pdf
                ? includePhotos
                    ? "У вас нет доступа к PDF-документам с фотографиями."
                    : "У вас нет доступа к PDF-документам."
                : includePhotos
                    ? "У вас нет доступа к Word-документам с фотографиями."
                    : "У вас нет доступа к Word-документам.";
            return DocumentExportResult.AccessDenied(message);
        }

        var maintenance = await maintenanceQueryService.GetById(userId, organizationId, maintenanceId);
        if (maintenance == null)
        {
            return DocumentExportResult.NotFound("Обслуживание не найдено");
        }

        var vehicle = await vehicleService.GetById(userId, organizationId, maintenance.VehicleId);
        if (vehicle == null)
        {
            return DocumentExportResult.NotFound("Техника не найдена");
        }

        var organization = await organizationService.GetByIdAsync(userId, organizationId);
        var documentDate = MaintenanceDocumentFacts.GetScenarioDocumentDate(maintenance);
        var documentNumber = MaintenanceDocumentFacts.BuildScenarioDocumentNumber(maintenance.Id, documentDate);
        byte[] documentBytes;
        string fileName;
        string contentType;

        if (format == DocumentFormat.Pdf)
        {
            var model = ScenarioDocumentModelAdapter.CreatePdf(
                organization,
                vehicle,
                maintenance,
                documentKindCode,
                template.Name,
                documentNumber,
                documentDate);
            model.Photos = includePhotos
                ? await documentPhotoLoader.LoadPdfMaintenancePhotosAsync(userId, organizationId, maintenance.Id)
                : [];
            documentBytes = await pdfGeneratorService.GenerateScenarioMaintenanceDocumentPdfAsync(model);
            contentType = "application/pdf";
            fileName = ScenarioDocumentWorkflow.BuildFileName(template.Name, vehicle, documentDate, "pdf");
        }
        else
        {
            var model = ScenarioDocumentModelAdapter.CreateWord(
                organization,
                vehicle,
                maintenance,
                documentKindCode,
                template.Name,
                documentNumber,
                documentDate);
            model.Photos = includePhotos
                ? await documentPhotoLoader.LoadWordPhotosAsync(userId, maintenance.PhotoIds)
                : [];
            documentBytes = await docxGeneratorService.GenerateScenarioMaintenanceDocumentDocxAsync(model);
            contentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
            fileName = ScenarioDocumentWorkflow.BuildFileName(template.Name, vehicle, documentDate, "docx");
        }

        var formatName = format == DocumentFormat.Pdf ? "PDF" : "Word";
        await maintenanceDocumentService.TrackDocumentGenerated(
            userId,
            organizationId,
            maintenance.Id,
            $"Сформирован сценарный документ {formatName}: {template.Name}",
            $"Формат: {formatName}\nШаблон: {template.Name}\nТип: {documentKindCode}\nФотографии: {(includePhotos ? "включены" : "не включены")}");

        return DocumentExportResult.File(documentBytes, contentType, fileName);
    }

    /// <summary> Формирует имя файла документа сценария. </summary>
    private static string BuildFileName(
        string templateName,
        GetVehicleDto vehicle,
        DateTime documentDate,
        string extension)
    {
        var templateFileName = MaintenanceDocumentFacts.SanitizeFileName(templateName);
        var vehicleTitle = VehicleDisplayName.Format(vehicle.BrandName, vehicle.ModelName, vehicle.RegistrationPlate);
        var vehicleFileName = MaintenanceDocumentFacts.SanitizeFileName(vehicleTitle);

        return $"{templateFileName}_{vehicleFileName}_{documentDate:yyyyMMdd}.{extension}";
    }

    /// <summary> Форматы экспорта документа сценария. </summary>
    private enum DocumentFormat
    {
        /// <summary> Экспорт в PDF. </summary>
        Pdf,

        /// <summary> Экспорт в Word. </summary>
        Word
    }
}
