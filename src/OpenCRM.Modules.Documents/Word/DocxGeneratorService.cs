using OpenCRM.Services.Interfaces.Word;
using OpenCRM.Services.Interfaces.Word.Dtos;
using OpenCRM.Modules.Documents.Word.Rendering;

namespace OpenCRM.Modules.Documents.Word;

/// <summary> Генерирует Word-документы по готовым моделям. </summary>
public class DocxGeneratorService(Helpers.ProjectBrand? brand = null) : IDocxGeneratorService
{
    /// <summary> Формирует Word-документ истории техники. </summary>
    public Task<byte[]> GenerateVehicleHistoryDocxAsync(WordVehicleModel model, bool includePhotos = false)
    {
        return new VehicleHistoryDocxRenderer(brand?.DisplayName ?? Helpers.ProjectBrand.DefaultName).GenerateVehicleHistoryDocxAsync(model, includePhotos);
    }

    /// <summary> Формирует Word-заказ-наряд на обслуживание. </summary>
    public Task<byte[]> GenerateMaintenanceWorkOrderDocxAsync(WordMaintenanceWorkActModel model)
    {
        return new MaintenanceOrderDocxRenderer(brand?.DisplayName ?? Helpers.ProjectBrand.DefaultName).GenerateMaintenanceWorkOrderDocxAsync(model);
    }

    /// <summary> Формирует Word-документ отчёта по обслуживанию. </summary>
    public Task<byte[]> GenerateMaintenanceDocxAsync(WordMaintenanceModel model, bool includePhotos = false)
    {
        return new MaintenanceReportDocxRenderer(brand?.DisplayName ?? Helpers.ProjectBrand.DefaultName).GenerateMaintenanceDocxAsync(model, includePhotos);
    }

    /// <summary> Формирует Word-документ обслуживания по шаблону сценария. </summary>
    public Task<byte[]> GenerateScenarioMaintenanceDocumentDocxAsync(WordScenarioMaintenanceDocumentModel model)
    {
        return new ScenarioMaintenanceDocxRenderer(brand?.DisplayName ?? Helpers.ProjectBrand.DefaultName).GenerateScenarioMaintenanceDocumentDocxAsync(model);
    }

    /// <summary> Формирует Word-акт выполненных работ по обслуживанию. </summary>
    public Task<byte[]> GenerateMaintenanceWorkActDocxAsync(WordMaintenanceWorkActModel model)
    {
        return new MaintenanceOrderDocxRenderer(brand?.DisplayName ?? Helpers.ProjectBrand.DefaultName).GenerateMaintenanceWorkActDocxAsync(model);
    }
}
