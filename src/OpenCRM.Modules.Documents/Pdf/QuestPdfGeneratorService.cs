using OpenCRM.Services.Interfaces.Pdf;
using OpenCRM.Services.Interfaces.Pdf.Dtos;
using OpenCRM.Modules.Documents.Pdf.Rendering;

namespace OpenCRM.Modules.Documents.Pdf;

/// <summary> Сервис генерации PDF-документов на основе QuestPDF. </summary>
public class QuestPdfGeneratorService(Helpers.ProjectBrand? brand = null) : IPdfGeneratorService
{
    /// <summary> Статический конструктор с лицензированием QuestPDF. </summary>
    static QuestPdfGeneratorService()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
    }

    /// <summary> Генерирует PDF-документ приёма обслуживания. </summary>
    public Task<byte[]> GenerateMaintenanceIntakePdfAsync(MaintenanceIntakePdfModel model)
    {
        return new MaintenanceIntakePdfRenderer().GenerateMaintenanceIntakePdfAsync(model);
    }

    /// <summary> Генерирует PDF-заказ-наряд по обслуживанию. </summary>
    public Task<byte[]> GenerateMaintenanceWorkOrderPdfAsync(MaintenanceWorkActPdfModel model)
    {
        return new MaintenanceOrderPdfRenderer(brand?.DisplayName ?? Helpers.ProjectBrand.DefaultName).GenerateMaintenanceWorkOrderPdfAsync(model);
    }

    /// <summary> Генерирует PDF-отчёт по обслуживанию. </summary>
    public Task<byte[]> GenerateMaintenancePdfAsync(MaintenancePdfModel model, bool includePhotos)
    {
        return new MaintenanceReportPdfRenderer(brand?.DisplayName ?? Helpers.ProjectBrand.DefaultName).GenerateMaintenancePdfAsync(model, includePhotos);
    }

    /// <summary> Генерирует PDF-отчёт по технике. </summary>
    public Task<byte[]> GenerateVehiclePdfAsync(VehiclePdfModel model, bool includePhotos)
    {
        return new VehicleHistoryPdfRenderer(brand?.DisplayName ?? Helpers.ProjectBrand.DefaultName).GenerateVehiclePdfAsync(model, includePhotos);
    }

    /// <summary> Генерирует PDF-акт выполненных работ. </summary>
    public Task<byte[]> GenerateMaintenanceWorkActPdfAsync(MaintenanceWorkActPdfModel model)
    {
        return new MaintenanceOrderPdfRenderer(brand?.DisplayName ?? Helpers.ProjectBrand.DefaultName).GenerateMaintenanceWorkActPdfAsync(model);
    }

    /// <summary> Генерирует PDF-документ сценария обслуживания. </summary>
    public Task<byte[]> GenerateScenarioMaintenanceDocumentPdfAsync(ScenarioMaintenanceDocumentPdfModel model)
    {
        return new ScenarioMaintenancePdfRenderer(brand?.DisplayName ?? Helpers.ProjectBrand.DefaultName).GenerateScenarioMaintenanceDocumentPdfAsync(model);
    }
}
