using OpenCRM.Services.Interfaces.Documents.Dtos;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Modules.Maintenance.Contracts.Photos;
using OpenCRM.Services.Interfaces.Pdf.Dtos;
using OpenCRM.Services.Interfaces.Word.Dtos;
using Moq;

namespace OpenCRM.Modules.Documents.Tests.Unit;

public sealed class DocumentModelSnapshotTests
{
    [Theory]
    [InlineData("pdf-vehicle")]
    [InlineData("word-vehicle")]
    [InlineData("pdf-maintenance")]
    [InlineData("word-maintenance")]
    [InlineData("pdf-intake")]
    [InlineData("pdf-act")]
    [InlineData("word-act")]
    [InlineData("pdf-order")]
    [InlineData("word-order")]
    [InlineData("pdf-scenario")]
    [InlineData("word-scenario")]
    public async Task ExportPreservesCompleteModelBeforeModuleDecomposition(string operation)
    {
        var actual = await DocumentModelSnapshotTests.CaptureAsync(operation);
        var snapshotPath = Path.Combine(AppContext.BaseDirectory, "Snapshots", operation + ".json");
        var expected = JsonNode.Parse(await File.ReadAllTextAsync(snapshotPath));

        Assert.True(JsonNode.DeepEquals(expected, actual), $"Изменилась модель {operation}: {actual}");
    }

    internal static async Task<JsonNode> CaptureAsync(string operation)
    {
        var fixture = new DocumentExportTestFixture();
        fixture.Maintenance.Status = MaintenanceStatus.Completed;
        fixture.AllowPhotoAccess();
        fixture.ConfigurePhoto([1, 2, 3]);
        fixture.VehiclePhotoId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        fixture.Maintenance.PhotoIds = [fixture.VehiclePhotoId.Value];
        fixture.SetMaintenancePhotos(new MaintenancePhotoDto(fixture.VehiclePhotoId.Value, "",
            MaintenancePhotoStage.Before, new DateTime(2025, 2, 3)));
        fixture.SetTemplate(OrganizationScenarioDocumentKindCodes.PhotoReport, "Исторический фотоотчёт");
        fixture.AddVehicleHistory(DocumentExportTestFixture.CreateHistory(
            Guid.Parse("66666666-6666-6666-6666-666666666666"), "Историческая работа", 1500.25m,
            new DateTime(2025, 2, 3)));
        object? model = null;
        fixture.PdfGenerator.Setup(generator => generator.GenerateVehiclePdfAsync(It.IsAny<VehiclePdfModel>(), true))
            .Callback<VehiclePdfModel, bool>((value, _) => model = value).ReturnsAsync([1]);
        fixture.DocxGenerator.Setup(generator => generator.GenerateVehicleHistoryDocxAsync(It.IsAny<WordVehicleModel>(), true))
            .Callback<WordVehicleModel, bool>((value, _) => model = value).ReturnsAsync([1]);
        fixture.PdfGenerator.Setup(generator => generator.GenerateMaintenancePdfAsync(It.IsAny<MaintenancePdfModel>(), true))
            .Callback<MaintenancePdfModel, bool>((value, _) => model = value).ReturnsAsync([1]);
        fixture.DocxGenerator.Setup(generator => generator.GenerateMaintenanceDocxAsync(It.IsAny<WordMaintenanceModel>(), true))
            .Callback<WordMaintenanceModel, bool>((value, _) => model = value).ReturnsAsync([1]);
        fixture.PdfGenerator.Setup(generator => generator.GenerateMaintenanceIntakePdfAsync(It.IsAny<MaintenanceIntakePdfModel>()))
            .Callback<MaintenanceIntakePdfModel>(value => model = value).ReturnsAsync([1]);
        fixture.PdfGenerator.Setup(generator => generator.GenerateMaintenanceWorkActPdfAsync(It.IsAny<MaintenanceWorkActPdfModel>()))
            .Callback<MaintenanceWorkActPdfModel>(value => model = value).ReturnsAsync([1]);
        fixture.DocxGenerator.Setup(generator => generator.GenerateMaintenanceWorkActDocxAsync(It.IsAny<WordMaintenanceWorkActModel>()))
            .Callback<WordMaintenanceWorkActModel>(value => model = value).ReturnsAsync([1]);
        fixture.PdfGenerator.Setup(generator => generator.GenerateMaintenanceWorkOrderPdfAsync(It.IsAny<MaintenanceWorkActPdfModel>()))
            .Callback<MaintenanceWorkActPdfModel>(value => model = value).ReturnsAsync([1]);
        fixture.DocxGenerator.Setup(generator => generator.GenerateMaintenanceWorkOrderDocxAsync(It.IsAny<WordMaintenanceWorkActModel>()))
            .Callback<WordMaintenanceWorkActModel>(value => model = value).ReturnsAsync([1]);
        fixture.PdfGenerator.Setup(generator => generator.GenerateScenarioMaintenanceDocumentPdfAsync(It.IsAny<ScenarioMaintenanceDocumentPdfModel>()))
            .Callback<ScenarioMaintenanceDocumentPdfModel>(value => model = value).ReturnsAsync([1]);
        fixture.DocxGenerator.Setup(generator => generator.GenerateScenarioMaintenanceDocumentDocxAsync(It.IsAny<WordScenarioMaintenanceDocumentModel>()))
            .Callback<WordScenarioMaintenanceDocumentModel>(value => model = value).ReturnsAsync([1]);

        var userId = DocumentExportTests.UserId;
        var vehicleId = DocumentExportTests.VehicleId;
        var maintenanceId = DocumentExportTests.MaintenanceId;
        var organizationId = DocumentExportTests.OrganizationId;
        var templateId = DocumentExportTests.TemplateId;

        var result = operation switch
        {
            "pdf-vehicle" => await fixture.Pdf.GenerateVehiclePdf(userId, vehicleId, true, organizationId),
            "word-vehicle" => await fixture.Word.GenerateVehicleWord(userId, vehicleId, true, organizationId),
            "pdf-maintenance" => await fixture.Pdf.GenerateMaintenancePdf(userId, maintenanceId, true, organizationId),
            "word-maintenance" => await fixture.Word.GenerateMaintenanceWord(userId, maintenanceId, true, organizationId),
            "pdf-intake" => await fixture.Pdf.PrintMaintenanceIntake(userId, maintenanceId, organizationId),
            "pdf-act" => await fixture.Pdf.GenerateMaintenanceWorkActPdf(userId, maintenanceId, organizationId),
            "word-act" => await fixture.Word.GenerateMaintenanceWorkActWord(userId, maintenanceId, organizationId),
            "pdf-order" => await fixture.Pdf.GenerateMaintenanceWorkOrderPdf(userId, maintenanceId, organizationId),
            "word-order" => await fixture.Word.GenerateMaintenanceWorkOrderWord(userId, maintenanceId, organizationId),
            "pdf-scenario" => await fixture.Pdf.GenerateScenarioMaintenanceDocumentPdf(userId, maintenanceId, templateId, organizationId),
            "word-scenario" => await fixture.Word.GenerateScenarioMaintenanceDocumentWord(userId, maintenanceId, templateId, organizationId),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

        Assert.Equal(DocumentExportOutcome.File, result.Outcome);
        Assert.NotNull(model);
        var snapshot = JsonSerializer.SerializeToNode(model)!;
        var generatedDate = snapshot["GeneratedDate"]!.GetValue<string>();
        var dateFormat = operation == "pdf-intake" ? "dd.MM.yyyy HH:mm 'UTC'" : "dd.MM.yyyy HH:mm";
        Assert.True(DateTime.TryParseExact(generatedDate, dateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _));
        snapshot.AsObject().Remove("GeneratedDate");

        return snapshot;
    }
}
