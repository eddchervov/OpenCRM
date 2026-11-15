using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Modules.Documents.Pdf;
using OpenCRM.Modules.Documents.Word;
using OpenCRM.Services.Interfaces.Documents;
using OpenCRM.Services.Interfaces.Pdf;
using OpenCRM.Services.Interfaces.Word;

namespace OpenCRM.Modules.Documents;

/// <summary> Расширение регистрации сервисов модуля документов. </summary>
public static class DocumentServiceCollectionExtensions
{
    /// <summary> Регистрирует сервисы модуля документов в контейнере. </summary>
    public static IServiceCollection AddOpenCRMDocuments(this IServiceCollection services)
    {
        services.AddScoped<IVehicleDocumentDataLoader, VehicleDocumentDataLoader>();
        services.AddScoped<IMaintenanceReportDataLoader, MaintenanceReportDataLoader>();
        services.AddScoped<DocumentExportAccess>();
        services.AddScoped<DocumentPhotoLoader>();
        services.AddScoped<VehicleReportWorkflow>();
        services.AddScoped<MaintenanceReportWorkflow>();
        services.AddScoped<WorkDocumentsWorkflow>();
        services.AddScoped<ScenarioDocumentWorkflow>();
        services.AddScoped<MaintenanceIntakeWorkflow>();
        services.AddScoped<IPdfGeneratorService, QuestPdfGeneratorService>();
        services.AddScoped<IPdfDocumentExportService, PdfDocumentExportService>();
        services.AddScoped<IDocxGeneratorService, DocxGeneratorService>();
        services.AddScoped<IWordDocumentExportService, WordDocumentExportService>();

        return services;
    }
}
