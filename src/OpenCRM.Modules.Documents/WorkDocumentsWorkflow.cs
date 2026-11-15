using OpenCRM.Services.Interfaces.Documents.Dtos;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Modules.Organizations.Contracts.Dtos;
using Helpers;
using OpenCRM.Domain.Enums;
using OpenCRM.Services.Interfaces.Documents;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using OpenCRM.Services.Interfaces.Pdf;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Vehicles.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Word;

namespace OpenCRM.Modules.Documents;

/// <summary> Формирует экспортные акты и заказ-наряды по обслуживанию. </summary>
public sealed class WorkDocumentsWorkflow(
    IPdfGeneratorService pdfGeneratorService,
    IDocxGeneratorService docxGeneratorService,
    IMaintenanceQueryService maintenanceQueryService,
    IMaintenanceDocumentService maintenanceDocumentService,
    IVehicleQueryService vehicleService,
    IOrganizationQueryService organizationService,
    DocumentExportAccess documentExportAccess)
{
    /// <summary> Формирует PDF-акт выполненных работ. </summary>
    public Task<DocumentExportResult> GenerateMaintenanceWorkActPdf(string userId, Guid maintenanceId, Guid organizationId) =>
        this.GenerateAsync(userId, maintenanceId, organizationId, WorkDocumentKind.Act, DocumentFormat.Pdf);

    /// <summary> Формирует PDF-заказ-наряд. </summary>
    public Task<DocumentExportResult> GenerateMaintenanceWorkOrderPdf(string userId, Guid maintenanceId, Guid organizationId) =>
        this.GenerateAsync(userId, maintenanceId, organizationId, WorkDocumentKind.Order, DocumentFormat.Pdf);

    /// <summary> Формирует Word-акт выполненных работ. </summary>
    public Task<DocumentExportResult> GenerateMaintenanceWorkActWord(string userId, Guid maintenanceId, Guid organizationId) =>
        this.GenerateAsync(userId, maintenanceId, organizationId, WorkDocumentKind.Act, DocumentFormat.Word);

    /// <summary> Формирует Word-заказ-наряд. </summary>
    public Task<DocumentExportResult> GenerateMaintenanceWorkOrderWord(string userId, Guid maintenanceId, Guid organizationId) =>
        this.GenerateAsync(userId, maintenanceId, organizationId, WorkDocumentKind.Order, DocumentFormat.Word);

    /// <summary> Формирует рабочий документ в заданном виде и формате. </summary>
    private async Task<DocumentExportResult> GenerateAsync(
        string userId,
        Guid maintenanceId,
        Guid organizationId,
        WorkDocumentKind kind,
        DocumentFormat format)
    {
        var workspaceAccess = await documentExportAccess.GetWorkspaceAccessAsync(userId, organizationId);
        if (!workspaceAccess.ExportsEnabled)
        {
            return DocumentExportResult.AccessDenied(
                workspaceAccess.ReadOnlyMessage ?? "Выгрузки для этого рабочего пространства временно недоступны.");
        }

        var hasDocumentAccess = format == DocumentFormat.Pdf
            ? await documentExportAccess.HasPdfAccessAsync(userId, organizationId, includePhotos: false)
            : await documentExportAccess.HasWordAccessAsync(userId, organizationId, includePhotos: false);
        if (!hasDocumentAccess)
        {
            var message = format == DocumentFormat.Pdf
                ? "У вас нет доступа к PDF-документам."
                : "У вас нет доступа к Word-документам.";
            return DocumentExportResult.AccessDenied(message);
        }

        var preparation = await this.LoadWorkDocumentDataAsync(
            userId,
            maintenanceId,
            organizationId,
            kind == WorkDocumentKind.Act);
        if (preparation.Error is not null)
        {
            return preparation.Error;
        }

        var maintenance = preparation.Maintenance!;
        var vehicle = preparation.Vehicle!;
        var organization = preparation.Organization!;
        var documentDate = kind == WorkDocumentKind.Act
            ? MaintenanceDocumentFacts.GetWorkActDate(maintenance)
            : MaintenanceDocumentFacts.GetWorkOrderDate(maintenance);
        var documentNumber = kind == WorkDocumentKind.Act
            ? (await maintenanceDocumentService.EnsureWorkActNumber(userId, organizationId, maintenanceId)).ToString()
            : MaintenanceDocumentFacts.BuildWorkOrderNumber(maintenance.Id, documentDate);
        byte[] documentBytes;
        string fileName;
        string contentType;

        if (format == DocumentFormat.Pdf)
        {
            var model = WorkDocumentModelAdapter.CreatePdf(organization, vehicle, maintenance, documentDate, documentNumber);
            documentBytes = kind == WorkDocumentKind.Act
                ? await pdfGeneratorService.GenerateMaintenanceWorkActPdfAsync(model)
                : await pdfGeneratorService.GenerateMaintenanceWorkOrderPdfAsync(model);
            contentType = "application/pdf";
            fileName = WorkDocumentsWorkflow.BuildFileName(kind, format, vehicle, documentDate);
        }
        else
        {
            var model = WorkDocumentModelAdapter.CreateWord(organization, vehicle, maintenance, documentDate, documentNumber);
            documentBytes = kind == WorkDocumentKind.Act
                ? await docxGeneratorService.GenerateMaintenanceWorkActDocxAsync(model)
                : await docxGeneratorService.GenerateMaintenanceWorkOrderDocxAsync(model);
            contentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
            fileName = WorkDocumentsWorkflow.BuildFileName(kind, format, vehicle, documentDate);
        }

        var formatName = WorkDocumentsWorkflow.GetFormatName(format);
        var historyTitle = kind == WorkDocumentKind.Act
            ? $"Сформирован акт выполненных работ {formatName}"
            : $"Сформирован заказ-наряд {formatName}";
        var numberLabel = kind == WorkDocumentKind.Act ? "Номер акта" : "Номер заказ-наряда";
        await maintenanceDocumentService.TrackDocumentGenerated(
            userId,
            organizationId,
            maintenance.Id,
            historyTitle,
            $"Формат: {formatName}\n{numberLabel}: {documentNumber}");

        return DocumentExportResult.File(documentBytes, contentType, fileName);
    }

    /// <summary> Загружает данные для формирования рабочего документа с проверкой доступа. </summary>
    private async Task<(
        DocumentExportResult? Error,
        GetMaintenanceByIdDto? Maintenance,
        GetVehicleDto? Vehicle,
        OrganizationDto? Organization)> LoadWorkDocumentDataAsync(
        string userId,
        Guid maintenanceId,
        Guid organizationId,
        bool mustBeCompleted)
    {
        var maintenance = await maintenanceQueryService.GetById(userId, organizationId, maintenanceId);
        if (maintenance == null)
        {
            return (DocumentExportResult.NotFound("Обслуживание не найдено"), null, null, null);
        }

        if (mustBeCompleted && maintenance.Status != MaintenanceStatus.Completed)
        {
            return (DocumentExportResult.InvalidRequest("Акт можно сформировать только для выполненного обслуживания."), null, null, null);
        }

        var vehicle = await vehicleService.GetById(userId, organizationId, maintenance.VehicleId);
        if (vehicle == null)
        {
            return (DocumentExportResult.NotFound("Техника не найдена"), null, null, null);
        }

        var organization = await organizationService.GetByIdAsync(userId, organizationId);

        return (null, maintenance, vehicle, organization);
    }

    /// <summary> Формирует имя файла рабочего документа. </summary>
    private static string BuildFileName(
        WorkDocumentKind kind,
        DocumentFormat format,
        GetVehicleDto vehicle,
        DateTime documentDate)
    {
        var vehicleName = MaintenanceDocumentFacts.SanitizeFileName(
            VehicleDisplayName.Format(vehicle.BrandName, vehicle.ModelName, vehicle.RegistrationPlate));
        var prefix = kind == WorkDocumentKind.Act ? "Акт_обслуживание" : "Заказ_наряд";
        var extension = format == DocumentFormat.Pdf ? "pdf" : "docx";

        return $"{prefix}_{vehicleName}_{documentDate:yyyyMMdd}.{extension}";
    }

    /// <summary> Возвращает название формата документа. </summary>
    private static string GetFormatName(DocumentFormat format) => format == DocumentFormat.Pdf ? "PDF" : "Word";

    /// <summary> Виды рабочих документов по обслуживанию. </summary>
    private enum WorkDocumentKind
    {
        /// <summary> Акт выполненных работ. </summary>
        Act,

        /// <summary> Заказ-наряд. </summary>
        Order
    }

    /// <summary> Форматы экспорта рабочего документа. </summary>
    private enum DocumentFormat
    {
        /// <summary> Экспорт в PDF. </summary>
        Pdf,

        /// <summary> Экспорт в Word. </summary>
        Word
    }
}
