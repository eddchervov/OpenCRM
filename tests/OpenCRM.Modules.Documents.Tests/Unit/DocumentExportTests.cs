using OpenCRM.Services.Interfaces.Documents.Dtos;
using OpenCRM.Modules.Maintenance.Contracts.Photos;
using OpenCRM.Modules.Catalog.Contracts.Dtos;
using OpenCRM.Domain;
using System.Collections;
using OpenCRM.Domain.Enums;
using OpenCRM.Modules.Documents.Pdf;
using OpenCRM.Modules.Documents.Word;
using OpenCRM.Services.Interfaces.Documents;
using OpenCRM.Services.Interfaces.Features;
using OpenCRM.Services.Interfaces.FileStorages;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Media;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using OpenCRM.Services.Interfaces.Pdf;
using OpenCRM.Services.Interfaces.Pdf.Dtos;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Vehicles.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Word;
using OpenCRM.Services.Interfaces.Word.Dtos;
using Helpers;
using Moq;

namespace OpenCRM.Modules.Documents.Tests.Unit;

public sealed class DocumentExportTests
{
    internal static byte[] FileBytes { get; } = [1, 2, 3, 4];
    internal static Guid VehicleId { get; } = Guid.Parse("11111111-1111-1111-1111-111111111111");
    internal static Guid MaintenanceId { get; } = Guid.Parse("22222222-2222-2222-2222-222222222222");
    internal static Guid OrganizationId { get; } = Guid.Parse("33333333-3333-3333-3333-333333333333");
    internal static Guid TemplateId { get; } = Guid.Parse("44444444-4444-4444-4444-444444444444");
    internal const string UserId = "document-export-user";

    [Fact]
    public async Task FreeAllowsWorkDocumentsAndReportsInPdfAndWord()
    {
        var fixture = new DocumentExportTestFixture();
        fixture.OrganizationAccess.Setup(service => service.GetWorkspaceAccessAsync(UserId, OrganizationId))
            .ReturnsAsync(new OrganizationWorkspaceAccessDto { IsFreePlan = true, ExportsEnabled = true });
        fixture.OrganizationAccess.Setup(service => service.HasFeatureAsync(UserId, OrganizationId, It.IsAny<string>()))
            .ReturnsAsync((string _, Guid _, string code) => code is FeatureCodes.ExportPdf
                or FeatureCodes.ExportPdfWithPhotos or FeatureCodes.ExportWord or FeatureCodes.ExportWordWithPhotos);
        fixture.Maintenance.Status = MaintenanceStatus.Completed;
        fixture.Maintenance.CompletedDate = new DateTime(2026, 10, 28);

        Assert.Equal(DocumentExportOutcome.File, (await fixture.Pdf.GenerateMaintenanceWorkActPdf(UserId, MaintenanceId, OrganizationId)).Outcome);
        Assert.Equal(DocumentExportOutcome.File, (await fixture.Pdf.GenerateMaintenanceWorkActPdf(UserId, MaintenanceId, OrganizationId)).Outcome);
        Assert.Equal(DocumentExportOutcome.File, (await fixture.Pdf.GenerateMaintenanceWorkOrderPdf(UserId, MaintenanceId, OrganizationId)).Outcome);
        Assert.Equal(DocumentExportOutcome.File, (await fixture.Word.GenerateMaintenanceWorkOrderWord(UserId, MaintenanceId, OrganizationId)).Outcome);
        Assert.Equal(DocumentExportOutcome.File, (await fixture.Word.GenerateMaintenanceWorkActWord(UserId, MaintenanceId, OrganizationId)).Outcome);
        foreach (var includePhotos in new[] { false, true })
        {
            Assert.Equal(DocumentExportOutcome.File, (await fixture.Pdf.GenerateMaintenancePdf(UserId, MaintenanceId, includePhotos, OrganizationId)).Outcome);
            Assert.Equal(DocumentExportOutcome.File, (await fixture.Pdf.GenerateVehiclePdf(UserId, VehicleId, includePhotos, OrganizationId)).Outcome);
        }
    }

    [Fact]
    public async Task FreeCanReadHistoricalIntakeButCannotGenerateOneWithoutRecordedInspection()
    {
        var fixture = new DocumentExportTestFixture();
        fixture.OrganizationAccess.Setup(service => service.GetWorkspaceAccessAsync(UserId, OrganizationId))
            .ReturnsAsync(new OrganizationWorkspaceAccessDto { IsFreePlan = true, ExportsEnabled = true });
        fixture.OrganizationAccess.Setup(service => service.HasFeatureAsync(UserId, OrganizationId, It.IsAny<string>()))
            .ReturnsAsync((string _, Guid _, string code) => code == FeatureCodes.ExportPdf);

        Assert.Equal(DocumentExportOutcome.File, (await fixture.Pdf.PrintMaintenanceIntake(UserId, MaintenanceId, OrganizationId)).Outcome);
        fixture.Maintenance.IntakeRecordedAt = null;

        Assert.Equal(DocumentExportOutcome.NotFound, (await fixture.Pdf.PrintMaintenanceIntake(UserId, MaintenanceId, OrganizationId)).Outcome);
    }

    [Fact]
    public async Task PdfVehicleExportMapsHistoricalRowsAndReturnsPdfFile()
    {
        var fixture = new DocumentExportTestFixture();
        fixture.AddVehicleHistory(
            DocumentExportTestFixture.CreateHistory(MaintenanceId, "Первая работа", 1250.5m, new DateTime(2025, 2, 3)),
            DocumentExportTestFixture.CreateHistory(Guid.NewGuid(), "Вторая работа", 750, new DateTime(2025, 4, 5)));
        fixture.AllowPhotoAccess();
        fixture.VehiclePhotoId = Guid.NewGuid();
        var photo = new byte[] { 8, 7, 6 };
        fixture.ConfigurePhoto(photo);
        VehiclePdfModel? generatedModel = null;
        fixture.PdfGenerator
            .Setup(generator => generator.GenerateVehiclePdfAsync(It.IsAny<VehiclePdfModel>(), true))
            .Callback<VehiclePdfModel, bool>((model, _) => generatedModel = model)
            .ReturnsAsync(FileBytes);

        var result = await fixture.Pdf.GenerateVehiclePdf(UserId, VehicleId, includePhotos: true, organizationId: OrganizationId);

        AssertFileResult(result, "application/pdf", FileBytes);
        Assert.Contains("Отчет_", result.FileName);
        Assert.EndsWith(".pdf", result.FileName);
        Assert.NotNull(generatedModel);
        Assert.Equal("Мастерская", generatedModel.Vehicle.BrandName);
        Assert.Equal("Грузовик", generatedModel.Vehicle.ModelName);
        Assert.Equal("А123АА154", generatedModel.Vehicle.RegistrationPlate);
        Assert.Equal(VehicleMileageUnit.EngineHours, generatedModel.Vehicle.MileageUnit);
        Assert.Equal(photo, generatedModel.Vehicle.PhotoData);
        Assert.Equal(2, generatedModel.TotalMaintenanceCount);
        Assert.Equal(2000.5m, generatedModel.TotalSpent);
        Assert.Equal(new[] { "Первая работа", "Вторая работа" }, generatedModel.MaintenanceHistory.Select(item => item.MaintenanceDetails.Single().Name));
        Assert.Equal("03.02.2025", generatedModel.MaintenanceHistory[0].Date);
        fixture.VehicleService.As<IVehicleDocumentHistoryService>().Verify(service => service.TrackDocumentGenerated(
            UserId, OrganizationId, VehicleId, "Сформирован отчет по технике PDF", "Формат: PDF\nФотографии: включены"), Times.Once);
        fixture.OrganizationAccess.Verify(service => service.EnsureWorkspaceCanReadAsync(
            UserId, OrganizationId, OrganizationPermissionCodes.DocumentsExport), Times.Once);
    }

    [Fact]
    public async Task PdfMaintenanceExportMapsSnapshotAndPhotoStages()
    {
        var fixture = new DocumentExportTestFixture();
        fixture.AllowPhotoAccess();
        fixture.SetMaintenancePhotos(
            new MaintenancePhotoDto(Guid.NewGuid(), "", MaintenancePhotoStage.After, new DateTime(2025, 2, 4)),
            new MaintenancePhotoDto(Guid.NewGuid(), "", MaintenancePhotoStage.Before, new DateTime(2025, 2, 3)));
        fixture.ConfigurePhoto([4, 5, 6]);
        MaintenancePdfModel? generatedModel = null;
        fixture.PdfGenerator
            .Setup(generator => generator.GenerateMaintenancePdfAsync(It.IsAny<MaintenancePdfModel>(), true))
            .Callback<MaintenancePdfModel, bool>((model, _) => generatedModel = model)
            .ReturnsAsync(FileBytes);

        var result = await fixture.Pdf.GenerateMaintenancePdf(UserId, MaintenanceId, includePhotos: true, organizationId: OrganizationId);

        AssertFileResult(result, "application/pdf", FileBytes);
        Assert.Contains("обслуживание", result.FileName);
        Assert.EndsWith(".pdf", result.FileName);
        Assert.NotNull(generatedModel);
        Assert.Equal("А123АА154", generatedModel.Vehicle.RegistrationPlate);
        Assert.Equal("Заменить узел", generatedModel.Maintenance.MaintenanceDetails.Single().Name);
        Assert.Equal(3, generatedModel.Maintenance.MaintenanceDetails.Single().Quantity);
        Assert.Equal(4250.75m, generatedModel.Maintenance.MaintenanceDetails.Single().Price);
        Assert.Equal("Приёмка: царапина", generatedModel.Maintenance.IntakeNotes);
        Assert.Equal(new[] { "До работы", "После работы" }, generatedModel.Maintenance.Photos.Select(photo => photo.Caption));
        Assert.All(generatedModel.Maintenance.Photos, photo => Assert.Equal(new byte[] { 4, 5, 6 }, photo.ImageData));
        fixture.MaintenanceDocumentService.Verify(service => service.TrackDocumentGenerated(
            UserId, OrganizationId, MaintenanceId, "Сформирован отчет обслуживания PDF", "Формат: PDF\nФотографии: включены"), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MaintenanceExportReadsVehiclePhotoBeforeEnumeratingRows(bool useWord)
    {
        var fixture = new DocumentExportTestFixture();
        var vehiclePhotoWasRead = false;
        fixture.AllowPhotoAccess();
        fixture.VehiclePhotoId = Guid.NewGuid();
        fixture.Maintenance.MaintenanceDetails = new CallbackEnumerable<MaintenanceDetailDto>(
            () => Assert.True(vehiclePhotoWasRead),
            [new MaintenanceDetailDto { Name = "Работа", Description = "", Price = 100, Quantity = 1 }]);
        fixture.FileStorage.Setup(service => service.GetFileAsync("documents/photo.jpg"))
            .ReturnsAsync(() => new ReadCallbackPhotoStream([1, 2, 3], () => vehiclePhotoWasRead = true));
        fixture.PdfGenerator.Setup(generator => generator.GenerateMaintenancePdfAsync(It.IsAny<MaintenancePdfModel>(), true))
            .ReturnsAsync(FileBytes);
        fixture.DocxGenerator.Setup(generator => generator.GenerateMaintenanceDocxAsync(It.IsAny<WordMaintenanceModel>(), true))
            .ReturnsAsync(FileBytes);

        var result = useWord
            ? await fixture.Word.GenerateMaintenanceWord(UserId, MaintenanceId, includePhotos: true, organizationId: OrganizationId)
            : await fixture.Pdf.GenerateMaintenancePdf(UserId, MaintenanceId, includePhotos: true, organizationId: OrganizationId);

        AssertFileResult(
            result,
            useWord ? "application/vnd.openxmlformats-officedocument.wordprocessingml.document" : "application/pdf",
            FileBytes);
        Assert.True(vehiclePhotoWasRead);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VehicleExportReadsVehiclePhotoBeforePreparingHistory(bool useWord)
    {
        var fixture = new DocumentExportTestFixture();
        fixture.AllowPhotoAccess();
        fixture.VehiclePhotoId = Guid.NewGuid();
        var firstPhotoWasRead = false;
        var first = DocumentExportTestFixture.CreateHistory(MaintenanceId, "Первая работа", 100, new DateTime(2025, 2, 3));
        var second = DocumentExportTestFixture.CreateHistory(Guid.NewGuid(), "Вторая работа", 200, new DateTime(2025, 2, 4));
        second.MaintenanceDetails = new CallbackEnumerable<MaintenanceDetailDto>(
            () => Assert.True(firstPhotoWasRead), second.MaintenanceDetails.ToArray());
        fixture.AddVehicleHistory(first, second);
        fixture.FileStorage.Setup(service => service.GetFileAsync("documents/photo.jpg"))
            .ReturnsAsync(() => new ReadCallbackPhotoStream([1, 2, 3], () => firstPhotoWasRead = true));

        var result = useWord
            ? await fixture.Word.GenerateVehicleWord(UserId, VehicleId, true, OrganizationId)
            : await fixture.Pdf.GenerateVehiclePdf(UserId, VehicleId, true, OrganizationId);

        AssertFileResult(result,
            useWord ? "application/vnd.openxmlformats-officedocument.wordprocessingml.document" : "application/pdf", FileBytes);
        Assert.True(firstPhotoWasRead);
    }

    [Fact]
    public async Task PdfIntakePrintUsesOnlyBeforePhotosAndDoesNotWriteHistory()
    {
        var fixture = new DocumentExportTestFixture();
        fixture.Maintenance.IntakeRecordedAt = new DateTime(2025, 2, 3, 10, 15, 0, DateTimeKind.Utc);
        fixture.AllowPhotoAccess();
        var beforeEarlyId = Guid.NewGuid();
        var beforeLateId = Guid.NewGuid();
        fixture.SetMaintenancePhotos(
            new MaintenancePhotoDto(Guid.NewGuid(), "", MaintenancePhotoStage.After, new DateTime(2025, 2, 3, 10, 0, 0)),
            new MaintenancePhotoDto(beforeLateId, "", MaintenancePhotoStage.Before, new DateTime(2025, 2, 3, 9, 0, 0)),
            new MaintenancePhotoDto(beforeEarlyId, "", MaintenancePhotoStage.Before, new DateTime(2025, 2, 3, 8, 0, 0)));
        fixture.ConfigurePhoto(beforeEarlyId, [1]);
        fixture.ConfigurePhoto(beforeLateId, [2]);
        MaintenanceIntakePdfModel? generatedModel = null;
        fixture.PdfGenerator
            .Setup(generator => generator.GenerateMaintenanceIntakePdfAsync(It.IsAny<MaintenanceIntakePdfModel>()))
            .Callback<MaintenanceIntakePdfModel>(model => generatedModel = model)
            .ReturnsAsync(FileBytes);

        var result = await fixture.Pdf.PrintMaintenanceIntake(UserId, MaintenanceId, OrganizationId);

        AssertFileResult(result, "application/pdf", FileBytes, hasFileName: false);
        Assert.Null(result.FileName);
        Assert.NotNull(generatedModel);
        Assert.Equal("СТО Кириллица", generatedModel.OrganizationName);
        Assert.Equal("Клиент", generatedModel.ClientName);
        Assert.Equal(654321, generatedModel.Mileage);
        Assert.Equal("Осмотрена", generatedModel.Completeness);
        Assert.Equal("Скол на стекле", generatedModel.VisibleDamage);
        Assert.Equal("Приёмка: царапина", generatedModel.Notes);
        Assert.Equal("До обслуживания", generatedModel.BeforePhotos[0].Caption);
        Assert.Equal(2, generatedModel.BeforePhotos.Count);
        Assert.Equal(new byte[] { 1 }, generatedModel.BeforePhotos[0].ImageData);
        Assert.Equal(new byte[] { 2 }, generatedModel.BeforePhotos[1].ImageData);
        fixture.MaintenanceDocumentService.Verify(service => service.TrackDocumentGenerated(
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task PdfIntakePrintFallsBackToRegularPdfAccessWithoutLoadingPhotos()
    {
        var fixture = new DocumentExportTestFixture();
        fixture.Maintenance.IntakeRecordedAt = new DateTime(2025, 2, 3, 10, 15, 0, DateTimeKind.Utc);
        fixture.OrganizationAccess
            .Setup(service => service.HasFeatureAsync(UserId, OrganizationId, FeatureCodes.ExportPdfWithPhotos))
            .ReturnsAsync(false);
        fixture.OrganizationAccess
            .Setup(service => service.HasFeatureAsync(UserId, OrganizationId, FeatureCodes.ExportPdf))
            .ReturnsAsync(true);

        var result = await fixture.Pdf.PrintMaintenanceIntake(UserId, MaintenanceId, OrganizationId);

        AssertFileResult(result, "application/pdf", FileBytes, hasFileName: false);
        Assert.Null(result.FileName);
        fixture.MaintenancePhotos.Verify(service => service.GetMaintenancePhotosAsync(
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>()), Times.Never);
        fixture.MaintenanceDocumentService.Verify(service => service.TrackDocumentGenerated(
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task PdfWorkActUsesCompletedDateNumberAndHistoricalAmounts()
    {
        var fixture = new DocumentExportTestFixture();
        fixture.OrganizationDetails.LegalName = "ООО Исполнитель";
        fixture.Maintenance.CompletedDate = new DateTime(2025, 3, 14);
        fixture.Maintenance.GrossAmount = 9000;
        fixture.Maintenance.DiscountAmount = 500;
        fixture.Maintenance.PaidAmount = 2500;
        fixture.Maintenance.DebtAmount = 6000;
        fixture.Maintenance.Status = MaintenanceStatus.Completed;
        MaintenanceWorkActPdfModel? generatedModel = null;
        fixture.PdfGenerator
            .Setup(generator => generator.GenerateMaintenanceWorkActPdfAsync(It.IsAny<MaintenanceWorkActPdfModel>()))
            .Callback<MaintenanceWorkActPdfModel>(model => generatedModel = model)
            .ReturnsAsync(FileBytes);

        var result = await fixture.Pdf.GenerateMaintenanceWorkActPdf(UserId, MaintenanceId, OrganizationId);

        AssertFileResult(result, "application/pdf", FileBytes);
        Assert.Equal("Акт_обслуживание_Мастерская_Грузовик_20250314.pdf", result.FileName);
        Assert.NotNull(generatedModel);
        Assert.Equal("42", generatedModel.ActNumber);
        Assert.Equal("14.03.2025", generatedModel.ActDate);
        Assert.Equal("ООО Исполнитель", generatedModel.ExecutorName);
        Assert.Equal(500m, generatedModel.DiscountAmount);
        Assert.Equal("ИНН", generatedModel.ExecutorInn);
        Assert.Equal("Мастерская Грузовик · Бортовой", generatedModel.VehicleTitle);
        Assert.Equal(654321, generatedModel.Mileage);
        Assert.Equal("м/ч", generatedModel.MileageUnit);
        Assert.Equal("Заменить узел", generatedModel.Works.Single().Name);
        Assert.Equal(3, generatedModel.Works.Single().Quantity);
        Assert.Equal(4250.75m, generatedModel.Works.Single().Price);
        fixture.MaintenanceDocumentService.Verify(service => service.EnsureWorkActNumber(UserId, OrganizationId, MaintenanceId), Times.Once);
        fixture.MaintenanceDocumentService.Verify(service => service.TrackDocumentGenerated(
            UserId, OrganizationId, MaintenanceId, "Сформирован акт выполненных работ PDF", "Формат: PDF\nНомер акта: 42"), Times.Once);
    }

    [Fact]
    public async Task PdfWorkOrderUsesPlannedDateAndFormatsThePlannedInterval()
    {
        var fixture = new DocumentExportTestFixture();
        fixture.Maintenance.DiscountAmount = 500;
        fixture.Maintenance.PlannedDate = new DateTime(2025, 6, 7);
        fixture.Maintenance.PlannedStartTime = TimeSpan.FromHours(9);
        fixture.Maintenance.PlannedEndDate = new DateTime(2025, 6, 8);
        fixture.Maintenance.PlannedEndTime = TimeSpan.FromHours(11);
        MaintenanceWorkActPdfModel? generatedModel = null;
        fixture.PdfGenerator
            .Setup(generator => generator.GenerateMaintenanceWorkOrderPdfAsync(It.IsAny<MaintenanceWorkActPdfModel>()))
            .Callback<MaintenanceWorkActPdfModel>(model => generatedModel = model)
            .ReturnsAsync(FileBytes);

        var result = await fixture.Pdf.GenerateMaintenanceWorkOrderPdf(UserId, MaintenanceId, OrganizationId);

        AssertFileResult(result, "application/pdf", FileBytes);
        Assert.Equal("Заказ_наряд_Мастерская_Грузовик_20250607.pdf", result.FileName);
        Assert.NotNull(generatedModel);
        Assert.Equal("20250607-22222222", generatedModel.ActNumber);
        Assert.Equal("07.06.2025", generatedModel.ActDate);
        Assert.Equal("07.06.2025 09:00 — 08.06.2025 11:00", generatedModel.PlannedPeriod);
        Assert.Equal("СТО Кириллица", generatedModel.ExecutorName);
        Assert.Equal(500m, generatedModel.DiscountAmount);
        Assert.Equal(3, generatedModel.Works.Single().Quantity);
        fixture.MaintenanceDocumentService.Verify(service => service.EnsureWorkActNumber(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>()), Times.Never);
        fixture.MaintenanceDocumentService.Verify(service => service.TrackDocumentGenerated(
            UserId, OrganizationId, MaintenanceId, "Сформирован заказ-наряд PDF", "Формат: PDF\nНомер заказ-наряда: 20250607-22222222"), Times.Once);
    }

    [Fact]
    public async Task PdfScenarioDocumentMapsTemplateFinanceAndPhotoReport()
    {
        var fixture = new DocumentExportTestFixture();
        fixture.SetTemplate(OrganizationScenarioDocumentKindCodes.PhotoReport, "Фотоотчёт / выдача");
        fixture.Maintenance.CompletedDate = new DateTime(2025, 7, 8);
        fixture.Maintenance.GrossAmount = 12000;
        fixture.Maintenance.DiscountAmount = 1000;
        fixture.Maintenance.PaidAmount = 5000;
        fixture.Maintenance.DebtAmount = 6000;
        fixture.Maintenance.MaterialCostAmount = 700;
        fixture.Maintenance.LaborCostAmount = 800;
        fixture.Maintenance.ProfitAmount = 9500;
        fixture.Maintenance.PaymentStatus = "partial";
        fixture.Maintenance.PhotoIds = [Guid.NewGuid()];
        fixture.AllowPhotoAccess();
        fixture.SetMaintenancePhotos(new MaintenancePhotoDto(Guid.NewGuid(), "", MaintenancePhotoStage.Before, new DateTime(2025, 7, 8)));
        fixture.ConfigurePhoto([2, 3, 4]);
        ScenarioMaintenanceDocumentPdfModel? generatedModel = null;
        fixture.PdfGenerator
            .Setup(generator => generator.GenerateScenarioMaintenanceDocumentPdfAsync(It.IsAny<ScenarioMaintenanceDocumentPdfModel>()))
            .Callback<ScenarioMaintenanceDocumentPdfModel>(model => generatedModel = model)
            .ReturnsAsync(FileBytes);

        var result = await fixture.Pdf.GenerateScenarioMaintenanceDocumentPdf(UserId, MaintenanceId, TemplateId, OrganizationId);

        AssertFileResult(result, "application/pdf", FileBytes);
        Assert.Equal("Фотоотчёт___выдача_Мастерская_Грузовик_20250708.pdf", result.FileName);
        Assert.NotNull(generatedModel);
        Assert.Equal(OrganizationScenarioDocumentKindCodes.PhotoReport, generatedModel.DocumentKindCode);
        Assert.Equal("Фотоотчёт / выдача", generatedModel.DocumentTitle);
        Assert.Equal("08.07.2025", generatedModel.DocumentDate);
        Assert.Equal("СТО Кириллица", generatedModel.ExecutorName);
        Assert.Equal(12000m, generatedModel.GrossAmount);
        Assert.Equal(1000m, generatedModel.DiscountAmount);
        Assert.Equal(5000m, generatedModel.PaidAmount);
        Assert.Equal(6000m, generatedModel.DebtAmount);
        Assert.Equal(700m, generatedModel.MaterialCostAmount);
        Assert.Equal(800m, generatedModel.LaborCostAmount);
        Assert.Equal(9500m, generatedModel.ProfitAmount);
        Assert.Equal("partial", generatedModel.PaymentStatus);
        Assert.Equal(new byte[] { 2, 3, 4 }, Assert.Single(generatedModel.Photos).ImageData);
        fixture.MaintenanceDocumentService.Verify(service => service.TrackDocumentGenerated(
            UserId,
            OrganizationId,
            MaintenanceId,
            "Сформирован сценарный документ PDF: Фотоотчёт / выдача",
            "Формат: PDF\nШаблон: Фотоотчёт / выдача\nТип: photo-report\nФотографии: включены"), Times.Once);
    }

    [Fact]
    public async Task WordVehicleExportPreservesHistoryAndLeavesPerMaintenancePhotosEmpty()
    {
        var fixture = new DocumentExportTestFixture();
        fixture.AddVehicleHistory(
            DocumentExportTestFixture.CreateHistory(MaintenanceId, "Ремонт двигателя", 1000.25m, new DateTime(2025, 2, 3)),
            DocumentExportTestFixture.CreateHistory(Guid.NewGuid(), "Диагностика", 400.50m, new DateTime(2025, 3, 4)));
        fixture.AllowPhotoAccess();
        fixture.VehiclePhotoId = Guid.NewGuid();
        fixture.ConfigurePhoto([4, 2]);
        WordVehicleModel? generatedModel = null;
        fixture.DocxGenerator
            .Setup(generator => generator.GenerateVehicleHistoryDocxAsync(It.IsAny<WordVehicleModel>(), true))
            .Callback<WordVehicleModel, bool>((model, _) => generatedModel = model)
            .ReturnsAsync(FileBytes);

        var result = await fixture.Word.GenerateVehicleWord(UserId, VehicleId, includePhotos: true, organizationId: OrganizationId);

        AssertFileResult(result, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", FileBytes);
        Assert.EndsWith(".docx", result.FileName);
        Assert.NotNull(generatedModel);
        Assert.Equal(new byte[] { 4, 2 }, generatedModel.Vehicle.PhotoData);
        Assert.Equal(2, generatedModel.TotalMaintenanceCount);
        Assert.Equal(1400.75m, generatedModel.TotalSpent);
        Assert.Equal(new[] { "Ремонт двигателя", "Диагностика" }, generatedModel.MaintenanceHistory.Select(item => item.MaintenanceDetails.Single().Name));
        Assert.All(generatedModel.MaintenanceHistory, item => Assert.Empty(item.Photos));
        fixture.VehicleService.As<IVehicleDocumentHistoryService>().Verify(service => service.TrackDocumentGenerated(
            UserId, OrganizationId, VehicleId, "Сформирован отчет по технике Word", "Формат: Word\nФотографии: включены"), Times.Once);
    }

    [Fact]
    public async Task WordMaintenanceExportPreservesTheCurrentEmptyPhotoPolicy()
    {
        var fixture = new DocumentExportTestFixture();
        fixture.AllowPhotoAccess();
        fixture.VehiclePhotoId = Guid.NewGuid();
        fixture.ConfigurePhoto([5, 5]);
        fixture.SetMaintenancePhotos(new MaintenancePhotoDto(Guid.NewGuid(), "", MaintenancePhotoStage.Before, new DateTime(2025, 2, 3)));
        WordMaintenanceModel? generatedModel = null;
        fixture.DocxGenerator
            .Setup(generator => generator.GenerateMaintenanceDocxAsync(It.IsAny<WordMaintenanceModel>(), true))
            .Callback<WordMaintenanceModel, bool>((model, _) => generatedModel = model)
            .ReturnsAsync(FileBytes);

        var result = await fixture.Word.GenerateMaintenanceWord(UserId, MaintenanceId, includePhotos: true, organizationId: OrganizationId);

        AssertFileResult(result, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", FileBytes);
        Assert.NotNull(generatedModel);
        Assert.Equal(new byte[] { 5, 5 }, generatedModel.Vehicle.PhotoData);
        Assert.Equal("Заменить узел", generatedModel.Maintenance.MaintenanceDetails.Single().Name);
        Assert.Equal(3, generatedModel.Maintenance.MaintenanceDetails.Single().Quantity);
        Assert.Empty(generatedModel.Maintenance.Photos);
        fixture.MaintenancePhotos.Verify(service => service.GetMaintenancePhotosAsync(
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>()), Times.Never);
        fixture.MaintenanceDocumentService.Verify(service => service.TrackDocumentGenerated(
            UserId, OrganizationId, MaintenanceId, "Сформирован отчет обслуживания Word", "Формат: Word\nФотографии: включены"), Times.Once);
    }

    [Fact]
    public async Task WordWorkActUsesCompletedDateAndStableActNumber()
    {
        var fixture = new DocumentExportTestFixture();
        fixture.Maintenance.Status = MaintenanceStatus.Completed;
        fixture.Maintenance.CompletedDate = new DateTime(2025, 8, 9);
        fixture.Maintenance.DiscountAmount = 500;
        WordMaintenanceWorkActModel? generatedModel = null;
        fixture.DocxGenerator
            .Setup(generator => generator.GenerateMaintenanceWorkActDocxAsync(It.IsAny<WordMaintenanceWorkActModel>()))
            .Callback<WordMaintenanceWorkActModel>(model => generatedModel = model)
            .ReturnsAsync(FileBytes);

        var result = await fixture.Word.GenerateMaintenanceWorkActWord(UserId, MaintenanceId, OrganizationId);

        AssertFileResult(result, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", FileBytes);
        Assert.Equal("Акт_обслуживание_Мастерская_Грузовик_20250809.docx", result.FileName);
        Assert.NotNull(generatedModel);
        Assert.Equal("42", generatedModel.ActNumber);
        Assert.Equal("09.08.2025", generatedModel.ActDate);
        Assert.Equal("СТО Кириллица", generatedModel.ExecutorName);
        Assert.Equal("ИНН", generatedModel.ExecutorInn);
        Assert.Equal(500m, generatedModel.DiscountAmount);
        Assert.Equal("Заменить узел", generatedModel.Works.Single().Name);
        fixture.MaintenanceDocumentService.Verify(service => service.EnsureWorkActNumber(UserId, OrganizationId, MaintenanceId), Times.Once);
        fixture.MaintenanceDocumentService.Verify(service => service.TrackDocumentGenerated(
            UserId, OrganizationId, MaintenanceId, "Сформирован акт выполненных работ Word", "Формат: Word\nНомер акта: 42"), Times.Once);
    }

    [Fact]
    public async Task WordWorkOrderUsesPlannedDateAndDoesNotReserveActNumber()
    {
        var fixture = new DocumentExportTestFixture();
        fixture.Maintenance.PlannedDate = new DateTime(2025, 9, 10);
        fixture.Maintenance.DiscountAmount = 500;
        fixture.Maintenance.PlannedStartTime = TimeSpan.FromHours(8);
        fixture.Maintenance.PlannedEndTime = TimeSpan.FromHours(10);
        WordMaintenanceWorkActModel? generatedModel = null;
        fixture.DocxGenerator
            .Setup(generator => generator.GenerateMaintenanceWorkOrderDocxAsync(It.IsAny<WordMaintenanceWorkActModel>()))
            .Callback<WordMaintenanceWorkActModel>(model => generatedModel = model)
            .ReturnsAsync(FileBytes);

        var result = await fixture.Word.GenerateMaintenanceWorkOrderWord(UserId, MaintenanceId, OrganizationId);

        AssertFileResult(result, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", FileBytes);
        Assert.Equal("Заказ_наряд_Мастерская_Грузовик_20250910.docx", result.FileName);
        Assert.NotNull(generatedModel);
        Assert.Equal("20250910-22222222", generatedModel.ActNumber);
        Assert.Equal("10.09.2025 08:00 — 10.09.2025 10:00", generatedModel.PlannedPeriod);
        Assert.Equal(500m, generatedModel.DiscountAmount);
        fixture.MaintenanceDocumentService.Verify(service => service.EnsureWorkActNumber(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>()), Times.Never);
        fixture.MaintenanceDocumentService.Verify(service => service.TrackDocumentGenerated(
            UserId, OrganizationId, MaintenanceId, "Сформирован заказ-наряд Word", "Формат: Word\nНомер заказ-наряда: 20250910-22222222"), Times.Once);
    }

    [Fact]
    public async Task WordWorkOrderUsesExplicitPlannedEndDateWhenProvided()
    {
        var fixture = new DocumentExportTestFixture();
        fixture.Maintenance.PlannedDate = new DateTime(2025, 9, 10);
        fixture.Maintenance.PlannedStartTime = TimeSpan.FromHours(8);
        fixture.Maintenance.PlannedEndDate = new DateTime(2025, 9, 11);
        fixture.Maintenance.PlannedEndTime = TimeSpan.FromHours(10);
        WordMaintenanceWorkActModel? generatedModel = null;
        fixture.DocxGenerator.Setup(generator => generator.GenerateMaintenanceWorkOrderDocxAsync(It.IsAny<WordMaintenanceWorkActModel>()))
            .Callback<WordMaintenanceWorkActModel>(model => generatedModel = model).ReturnsAsync(FileBytes);

        var result = await fixture.Word.GenerateMaintenanceWorkOrderWord(UserId, MaintenanceId, OrganizationId);

        Assert.Equal(DocumentExportOutcome.File, result.Outcome);
        Assert.Equal("10.09.2025 08:00 — 11.09.2025 10:00", generatedModel!.PlannedPeriod);
    }

    [Fact]
    public async Task WordScenarioDocumentPreservesTemplateAndLimitsPhotosToFirstTwelveIds()
    {
        var fixture = new DocumentExportTestFixture();
        fixture.SetTemplate(OrganizationScenarioDocumentKindCodes.InspectionAct, "Осмотр автомобиля");
        fixture.Maintenance.PlannedDate = null;
        fixture.Maintenance.GrossAmount = 12000;
        fixture.Maintenance.DiscountAmount = 1000;
        fixture.Maintenance.PaidAmount = 5000;
        fixture.Maintenance.DebtAmount = 6000;
        fixture.Maintenance.MaterialCostAmount = 700;
        fixture.Maintenance.LaborCostAmount = 800;
        fixture.Maintenance.ProfitAmount = 9500;
        fixture.Maintenance.PaymentStatus = "partial";
        var photoIds = Enumerable.Range(0, 13).Select(_ => Guid.NewGuid()).ToArray();
        fixture.Maintenance.PhotoIds = photoIds;
        fixture.AllowPhotoAccess();
        foreach (var (photoId, index) in photoIds.Select((photoId, index) => (photoId, index)))
        {
            fixture.ConfigurePhoto(photoId, [(byte)index]);
        }
        WordScenarioMaintenanceDocumentModel? generatedModel = null;
        fixture.DocxGenerator
            .Setup(generator => generator.GenerateScenarioMaintenanceDocumentDocxAsync(It.IsAny<WordScenarioMaintenanceDocumentModel>()))
            .Callback<WordScenarioMaintenanceDocumentModel>(model => generatedModel = model)
            .ReturnsAsync(FileBytes);

        var result = await fixture.Word.GenerateScenarioMaintenanceDocumentWord(UserId, MaintenanceId, TemplateId, OrganizationId);

        AssertFileResult(result, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", FileBytes);
        Assert.Equal("Осмотр_автомобиля_Мастерская_Грузовик_20250203.docx", result.FileName);
        Assert.NotNull(generatedModel);
        Assert.Equal(OrganizationScenarioDocumentKindCodes.InspectionAct, generatedModel.DocumentKindCode);
        Assert.Equal("Осмотр автомобиля", generatedModel.DocumentTitle);
        Assert.Equal("03.02.2025", generatedModel.DocumentDate);
        Assert.Equal(12000m, generatedModel.GrossAmount);
        Assert.Equal(1000m, generatedModel.DiscountAmount);
        Assert.Equal(5000m, generatedModel.PaidAmount);
        Assert.Equal(6000m, generatedModel.DebtAmount);
        Assert.Equal(700m, generatedModel.MaterialCostAmount);
        Assert.Equal(800m, generatedModel.LaborCostAmount);
        Assert.Equal(9500m, generatedModel.ProfitAmount);
        Assert.Equal("partial", generatedModel.PaymentStatus);
        Assert.Equal(12, generatedModel.Photos.Count);
        Assert.Equal(new byte[] { 0 }, generatedModel.Photos[0].ImageData);
        Assert.Equal(new byte[] { 11 }, generatedModel.Photos[^1].ImageData);
        fixture.MaintenanceDocumentService.Verify(service => service.TrackDocumentGenerated(
            UserId,
            OrganizationId,
            MaintenanceId,
            "Сформирован сценарный документ Word: Осмотр автомобиля",
            "Формат: Word\nШаблон: Осмотр автомобиля\nТип: inspection-act\nФотографии: включены"), Times.Once);
    }

    [Fact]
    public async Task PdfMaintenanceExportSkipsPhotoWithoutMediaAccess()
    {
        var fixture = new DocumentExportTestFixture();
        var photoId = Guid.NewGuid();
        fixture.SetMaintenancePhotos(new MaintenancePhotoDto(photoId, "", MaintenancePhotoStage.Before, new DateTime(2025, 2, 3)));
        fixture.Media.Setup(service => service.UserHasAccessToMediaAsync(UserId, photoId)).ReturnsAsync(false);
        MaintenancePdfModel? generatedModel = null;
        fixture.PdfGenerator
            .Setup(generator => generator.GenerateMaintenancePdfAsync(It.IsAny<MaintenancePdfModel>(), true))
            .Callback<MaintenancePdfModel, bool>((model, _) => generatedModel = model)
            .ReturnsAsync(FileBytes);

        var result = await fixture.Pdf.GenerateMaintenancePdf(UserId, MaintenanceId, includePhotos: true, organizationId: OrganizationId);

        AssertFileResult(result, "application/pdf", FileBytes);
        Assert.NotNull(generatedModel);
        Assert.Empty(generatedModel.Maintenance.Photos);
        fixture.Media.Verify(service => service.GetMediaPathAsync(UserId, photoId), Times.Never);
    }

    [Fact]
    public async Task PdfMaintenancePhotoCaptionIsNullForAnUnrecognizedStage()
    {
        var fixture = new DocumentExportTestFixture();
        fixture.AllowPhotoAccess();
        var photoId = Guid.NewGuid();
        fixture.SetMaintenancePhotos(new MaintenancePhotoDto(photoId, "", (MaintenancePhotoStage)999, new DateTime(2025, 2, 3)));
        fixture.ConfigurePhoto(photoId, [1, 2]);
        MaintenancePdfModel? generatedModel = null;
        fixture.PdfGenerator.Setup(generator => generator.GenerateMaintenancePdfAsync(It.IsAny<MaintenancePdfModel>(), true))
            .Callback<MaintenancePdfModel, bool>((model, _) => generatedModel = model).ReturnsAsync(FileBytes);

        var result = await fixture.Pdf.GenerateMaintenancePdf(UserId, MaintenanceId, includePhotos: true, organizationId: OrganizationId);

        Assert.Equal(DocumentExportOutcome.File, result.Outcome);
        Assert.Null(Assert.Single(generatedModel!.Maintenance.Photos).Caption);
    }

    [Theory]
    [InlineData("pdf", false, false)]
    [InlineData("pdf", false, true)]
    [InlineData("pdf", true, false)]
    [InlineData("pdf", true, true)]
    [InlineData("word", false, false)]
    [InlineData("word", false, true)]
    [InlineData("word", true, false)]
    [InlineData("word", true, true)]
    public async Task MaintenanceExportLoadsVehiclePhotoOnlyWhenRequestedAndAvailable(
        string format,
        bool includePhotos,
        bool hasVehiclePhoto)
    {
        var fixture = new DocumentExportTestFixture();
        var photoId = Guid.NewGuid();
        var photo = new byte[] { 7, 8 };
        if (hasVehiclePhoto)
        {
            fixture.VehiclePhotoId = photoId;
            fixture.AllowPhotoAccess();
            fixture.ConfigurePhoto(photo);
        }

        byte[]? generatedPhoto = null;
        fixture.PdfGenerator.Setup(generator => generator.GenerateMaintenancePdfAsync(It.IsAny<MaintenancePdfModel>(), includePhotos))
            .Callback<MaintenancePdfModel, bool>((model, _) => generatedPhoto = model.Vehicle.PhotoData).ReturnsAsync(FileBytes);
        fixture.DocxGenerator.Setup(generator => generator.GenerateMaintenanceDocxAsync(It.IsAny<WordMaintenanceModel>(), includePhotos))
            .Callback<WordMaintenanceModel, bool>((model, _) => generatedPhoto = model.Vehicle.PhotoData).ReturnsAsync(FileBytes);

        var result = format == "pdf"
            ? await fixture.Pdf.GenerateMaintenancePdf(UserId, MaintenanceId, includePhotos, OrganizationId)
            : await fixture.Word.GenerateMaintenanceWord(UserId, MaintenanceId, includePhotos, OrganizationId);

        Assert.Equal(DocumentExportOutcome.File, result.Outcome);
        Assert.Equal(includePhotos && hasVehiclePhoto ? photo : null, generatedPhoto);
    }

    [Fact]
    public async Task WordScenarioExportSkipsUnavailablePhotoAndStillGeneratesDocument()
    {
        var fixture = new DocumentExportTestFixture();
        fixture.SetTemplate(OrganizationScenarioDocumentKindCodes.PhotoReport, "Фотоотчёт");
        var photoId = Guid.NewGuid();
        fixture.Maintenance.PhotoIds = [photoId];
        fixture.Media.Setup(service => service.UserHasAccessToMediaAsync(UserId, photoId)).ReturnsAsync(false);
        WordScenarioMaintenanceDocumentModel? generatedModel = null;
        fixture.DocxGenerator
            .Setup(generator => generator.GenerateScenarioMaintenanceDocumentDocxAsync(It.IsAny<WordScenarioMaintenanceDocumentModel>()))
            .Callback<WordScenarioMaintenanceDocumentModel>(model => generatedModel = model)
            .ReturnsAsync(FileBytes);

        var result = await fixture.Word.GenerateScenarioMaintenanceDocumentWord(UserId, MaintenanceId, TemplateId, OrganizationId);

        AssertFileResult(result, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", FileBytes);
        Assert.NotNull(generatedModel);
        Assert.Empty(generatedModel.Photos);
        fixture.Media.Verify(service => service.GetMediaPathAsync(UserId, photoId), Times.Never);
    }

    [Theory]
    [InlineData("pdf-vehicle")]
    [InlineData("pdf-maintenance")]
    [InlineData("pdf-intake")]
    [InlineData("pdf-act")]
    [InlineData("pdf-work-order")]
    [InlineData("pdf-scenario")]
    [InlineData("word-vehicle")]
    [InlineData("word-maintenance")]
    [InlineData("word-act")]
    [InlineData("word-work-order")]
    [InlineData("word-scenario")]
    public async Task WorkspaceExportFlagDeniesEveryOperationBeforeDataLookup(string operation)
    {
        var fixture = new DocumentExportTestFixture(exportsEnabled: false);

        var result = await ExecuteOperation(fixture, operation);

        Assert.Equal(DocumentExportOutcome.AccessDenied, result.Outcome);
        fixture.OrganizationAccess.Verify(service => service.GetWorkspaceAccessAsync(UserId, OrganizationId), Times.Once);
        fixture.VehicleService.Verify(service => service.GetById(
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>()), Times.Never);
        fixture.MaintenanceQueryService.Verify(service => service.GetById(
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>()), Times.Never);
        fixture.ScenarioCatalog.Verify(service => service.GetAsync(
            It.IsAny<string>(), It.IsAny<Guid>()), Times.Never);
        fixture.VerifyNoGenerationOrHistory();
    }

    [Fact]
    public async Task PdfFeatureDenialPrecedesVehicleLookupAndUsesPhotoSpecificFeature()
    {
        var fixture = new DocumentExportTestFixture();
        fixture.OrganizationAccess.Setup(service => service.HasFeatureAsync(UserId, OrganizationId, FeatureCodes.ExportPdfWithPhotos)).ReturnsAsync(false);

        var result = await fixture.Pdf.GenerateVehiclePdf(UserId, VehicleId, includePhotos: true, organizationId: OrganizationId);

        Assert.Equal(DocumentExportOutcome.AccessDenied, result.Outcome);
        fixture.OrganizationAccess.Verify(service => service.HasFeatureAsync(UserId, OrganizationId, FeatureCodes.ExportPdfWithPhotos), Times.Once);
        fixture.MaintenanceQueryService.Verify(service => service.Get(
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>()), Times.Never);
        fixture.VehicleService.Verify(service => service.GetById(
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>()), Times.Never);
        fixture.VerifyNoGenerationOrHistory();
    }

    [Fact]
    public async Task WordPhotoFeatureDenialPrecedesMaintenanceLookup()
    {
        var fixture = new DocumentExportTestFixture();
        fixture.OrganizationAccess
            .Setup(service => service.HasFeatureAsync(UserId, OrganizationId, FeatureCodes.ExportWordWithPhotos))
            .ReturnsAsync(false);

        var result = await fixture.Word.GenerateMaintenanceWord(UserId, MaintenanceId, includePhotos: true, organizationId: OrganizationId);

        Assert.Equal(DocumentExportOutcome.AccessDenied, result.Outcome);
        fixture.MaintenanceQueryService.Verify(service => service.GetById(
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>()), Times.Never);
        fixture.VerifyNoGenerationOrHistory();
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData("word")]
    public async Task ScenarioPhotoFeatureDenialPrecedesMaintenanceLookup(string format)
    {
        var fixture = new DocumentExportTestFixture();
        fixture.SetTemplate(OrganizationScenarioDocumentKindCodes.PhotoReport, "Фотоотчёт");
        fixture.OrganizationAccess.Setup(service => service.HasFeatureAsync(
                UserId,
                OrganizationId,
                format == "pdf" ? FeatureCodes.ExportPdfWithPhotos : FeatureCodes.ExportWordWithPhotos))
            .ReturnsAsync(false);

        var result = format == "pdf"
            ? await fixture.Pdf.GenerateScenarioMaintenanceDocumentPdf(UserId, MaintenanceId, TemplateId, OrganizationId)
            : await fixture.Word.GenerateScenarioMaintenanceDocumentWord(UserId, MaintenanceId, TemplateId, OrganizationId);

        Assert.Equal(DocumentExportOutcome.AccessDenied, result.Outcome);
        fixture.MaintenanceQueryService.Verify(service => service.GetById(
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>()), Times.Never);
        fixture.VerifyNoGenerationOrHistory();
    }

    [Fact]
    public async Task WorkActRequiresCompletedMaintenanceInBothFormats()
    {
        var fixture = new DocumentExportTestFixture();

        var pdf = await fixture.Pdf.GenerateMaintenanceWorkActPdf(UserId, MaintenanceId, OrganizationId);
        var word = await fixture.Word.GenerateMaintenanceWorkActWord(UserId, MaintenanceId, OrganizationId);

        Assert.Equal(DocumentExportOutcome.InvalidRequest, pdf.Outcome);
        Assert.Equal(DocumentExportOutcome.InvalidRequest, word.Outcome);
        fixture.VehicleService.Verify(service => service.GetById(
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>()), Times.Never);
        fixture.MaintenanceDocumentService.Verify(service => service.EnsureWorkActNumber(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>()), Times.Never);
        fixture.VerifyNoGenerationOrHistory();
    }

    [Theory]
    [InlineData("pdf-maintenance")]
    [InlineData("pdf-act")]
    [InlineData("pdf-work-order")]
    [InlineData("pdf-scenario")]
    [InlineData("word-maintenance")]
    [InlineData("word-act")]
    [InlineData("word-work-order")]
    [InlineData("word-scenario")]
    [InlineData("pdf-intake")]
    public async Task MissingMaintenanceStopsEveryExportWithAMaintenanceGuard(string operation)
    {
        var fixture = new DocumentExportTestFixture();
        fixture.MaintenanceQueryService.Setup(service => service.GetById(UserId, OrganizationId, MaintenanceId))
            .ReturnsAsync((GetMaintenanceByIdDto)null!);

        var result = await ExecuteOperation(fixture, operation);

        Assert.Equal(DocumentExportOutcome.NotFound, result.Outcome);
        fixture.MaintenanceQueryService.Verify(service => service.GetById(UserId, OrganizationId, MaintenanceId), Times.Once);
        fixture.VehicleService.Verify(service => service.GetById(
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>()), Times.Never);
        fixture.VerifyNoGenerationOrHistory();
    }

    [Theory]
    [InlineData("pdf-maintenance")]
    [InlineData("pdf-intake")]
    [InlineData("pdf-act")]
    [InlineData("pdf-work-order")]
    [InlineData("pdf-scenario")]
    [InlineData("word-maintenance")]
    [InlineData("word-act")]
    [InlineData("word-work-order")]
    [InlineData("word-scenario")]
    public async Task MissingVehicleStopsEveryMaintenanceExportBeforeRendering(string operation)
    {
        var fixture = new DocumentExportTestFixture();
        fixture.Maintenance.Status = MaintenanceStatus.Completed;
        fixture.VehicleService.Setup(service => service.GetById(UserId, OrganizationId, VehicleId))
            .ReturnsAsync((GetVehicleDto)null!);

        var result = await ExecuteOperation(fixture, operation);

        Assert.Equal(DocumentExportOutcome.NotFound, result.Outcome);
        fixture.MaintenanceQueryService.Verify(service => service.GetById(UserId, OrganizationId, MaintenanceId), Times.Once);
        fixture.VehicleService.Verify(service => service.GetById(UserId, OrganizationId, VehicleId), Times.Once);
        fixture.VerifyNoGenerationOrHistory();
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData("word")]
    public async Task ScenarioDateFallsBackToMaintenanceDateWhenPlannedAndCompletedDatesAreMissing(string format)
    {
        var fixture = new DocumentExportTestFixture();
        fixture.Maintenance.CompletedDate = null;
        fixture.Maintenance.PlannedDate = null;
        ScenarioMaintenanceDocumentPdfModel? pdfModel = null;
        WordScenarioMaintenanceDocumentModel? wordModel = null;
        fixture.PdfGenerator.Setup(generator => generator.GenerateScenarioMaintenanceDocumentPdfAsync(It.IsAny<ScenarioMaintenanceDocumentPdfModel>()))
            .Callback<ScenarioMaintenanceDocumentPdfModel>(model => pdfModel = model).ReturnsAsync(FileBytes);
        fixture.DocxGenerator.Setup(generator => generator.GenerateScenarioMaintenanceDocumentDocxAsync(It.IsAny<WordScenarioMaintenanceDocumentModel>()))
            .Callback<WordScenarioMaintenanceDocumentModel>(model => wordModel = model).ReturnsAsync(FileBytes);

        var result = format == "pdf"
            ? await fixture.Pdf.GenerateScenarioMaintenanceDocumentPdf(UserId, MaintenanceId, TemplateId, OrganizationId)
            : await fixture.Word.GenerateScenarioMaintenanceDocumentWord(UserId, MaintenanceId, TemplateId, OrganizationId);

        Assert.Equal(DocumentExportOutcome.File, result.Outcome);
        Assert.Equal("03.02.2025", format == "pdf" ? pdfModel!.DocumentDate : wordModel!.DocumentDate);
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData("word")]
    public async Task VehicleExportTreatsMissingHistoricalPricesAsZero(string format)
    {
        var fixture = new DocumentExportTestFixture();
        var history = DocumentExportTestFixture.CreateHistory(MaintenanceId, "Работа без цены", 0, new DateTime(2025, 2, 3));
        history.Price = null;
        fixture.AddVehicleHistory(history);
        VehiclePdfModel? pdfModel = null;
        WordVehicleModel? wordModel = null;
        fixture.PdfGenerator.Setup(generator => generator.GenerateVehiclePdfAsync(It.IsAny<VehiclePdfModel>(), It.IsAny<bool>()))
            .Callback<VehiclePdfModel, bool>((model, _) => pdfModel = model).ReturnsAsync(FileBytes);
        fixture.DocxGenerator.Setup(generator => generator.GenerateVehicleHistoryDocxAsync(It.IsAny<WordVehicleModel>(), It.IsAny<bool>()))
            .Callback<WordVehicleModel, bool>((model, _) => wordModel = model).ReturnsAsync(FileBytes);

        var result = format == "pdf"
            ? await fixture.Pdf.GenerateVehiclePdf(UserId, VehicleId, organizationId: OrganizationId)
            : await fixture.Word.GenerateVehicleWord(UserId, VehicleId, organizationId: OrganizationId);

        Assert.Equal(DocumentExportOutcome.File, result.Outcome);
        Assert.Equal(0, format == "pdf" ? pdfModel!.TotalSpent : wordModel!.TotalSpent);
    }

    [Fact]
    public async Task FailedPdfAndWordGenerationDoesNotWriteDocumentHistory()
    {
        var pdfFixture = new DocumentExportTestFixture();
        pdfFixture.PdfGenerator
            .Setup(generator => generator.GenerateMaintenancePdfAsync(It.IsAny<MaintenancePdfModel>(), false))
            .ThrowsAsync(new InvalidOperationException("PDF renderer failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pdfFixture.Pdf.GenerateMaintenancePdf(UserId, MaintenanceId, organizationId: OrganizationId));

        pdfFixture.MaintenanceDocumentService.Verify(service => service.TrackDocumentGenerated(
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);

        var wordFixture = new DocumentExportTestFixture();
        wordFixture.DocxGenerator
            .Setup(generator => generator.GenerateMaintenanceDocxAsync(It.IsAny<WordMaintenanceModel>(), false))
            .ThrowsAsync(new InvalidOperationException("DOCX renderer failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            wordFixture.Word.GenerateMaintenanceWord(UserId, MaintenanceId, organizationId: OrganizationId));

        wordFixture.MaintenanceDocumentService.Verify(service => service.TrackDocumentGenerated(
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task EmptyHistoryStopsVehicleExportsBeforeGeneration()
    {
        var emptyFixture = new DocumentExportTestFixture();
        emptyFixture.AddVehicleHistory();

        var emptyPdf = await emptyFixture.Pdf.GenerateVehiclePdf(UserId, VehicleId, organizationId: OrganizationId);
        var emptyWord = await emptyFixture.Word.GenerateVehicleWord(UserId, VehicleId, organizationId: OrganizationId);

        Assert.Equal(DocumentExportOutcome.InvalidRequest, emptyPdf.Outcome);
        Assert.Equal(DocumentExportOutcome.InvalidRequest, emptyWord.Outcome);
        emptyFixture.VerifyNoGenerationOrHistory();
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData("word")]
    public async Task MissingVehicleStopsVehicleHistoryExportsBeforeLoadingHistory(string format)
    {
        var fixture = new DocumentExportTestFixture();
        fixture.VehicleService.Setup(service => service.GetById(UserId, OrganizationId, VehicleId))
            .ReturnsAsync((GetVehicleDto)null!);

        var result = format == "pdf"
            ? await fixture.Pdf.GenerateVehiclePdf(UserId, VehicleId, organizationId: OrganizationId)
            : await fixture.Word.GenerateVehicleWord(UserId, VehicleId, organizationId: OrganizationId);

        Assert.Equal(DocumentExportOutcome.NotFound, result.Outcome);
        fixture.MaintenanceQueryService.Verify(service => service.Get(
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>()), Times.Never);
        fixture.VerifyNoGenerationOrHistory();
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData("word")]
    public async Task VehicleExportUsesCustomReadOnlyMessageWhenWorkspaceExportsAreDisabled(string format)
    {
        const string readOnlyMessage = "Экспорт временно приостановлен.";
        var fixture = new DocumentExportTestFixture();
        fixture.OrganizationAccess.Setup(service => service.GetWorkspaceAccessAsync(UserId, OrganizationId))
            .ReturnsAsync(new OrganizationWorkspaceAccessDto
            {
                ExportsEnabled = false,
                ReadOnlyMessage = readOnlyMessage
            });

        var result = format == "pdf"
            ? await fixture.Pdf.GenerateVehiclePdf(UserId, VehicleId, organizationId: OrganizationId)
            : await fixture.Word.GenerateVehicleWord(UserId, VehicleId, organizationId: OrganizationId);

        Assert.Equal(DocumentExportOutcome.AccessDenied, result.Outcome);
        Assert.Equal(readOnlyMessage, result.Message);
        fixture.VehicleService.Verify(service => service.GetById(
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>()), Times.Never);
        fixture.VerifyNoGenerationOrHistory();
    }

    [Theory]
    [InlineData("word-vehicle", "У вас нет доступа к Word-экспорту.")]
    [InlineData("pdf-maintenance", "У вас нет доступа к PDF-экспорту.")]
    public async Task RegularExportFeatureDenialUsesNonPhotoMessage(string operation, string expectedMessage)
    {
        var fixture = new DocumentExportTestFixture();
        fixture.OrganizationAccess.Setup(service => service.HasFeatureAsync(UserId, OrganizationId, It.IsAny<string>()))
            .ReturnsAsync(false);

        var result = await ExecuteOperation(fixture, operation);

        Assert.Equal(DocumentExportOutcome.AccessDenied, result.Outcome);
        Assert.Equal(expectedMessage, result.Message);
        fixture.VehicleService.Verify(service => service.GetById(
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>()), Times.Never);
        fixture.MaintenanceQueryService.Verify(service => service.GetById(
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>()), Times.Never);
        fixture.VerifyNoGenerationOrHistory();
    }

    [Fact]
    public async Task MissingScenarioTemplateStopsBeforeMaintenanceLookup()
    {
        var fixture = new DocumentExportTestFixture();
        fixture.SetTemplate(OrganizationScenarioDocumentKindCodes.WorkOrder, "Неактивный шаблон", isActive: false);

        var pdf = await fixture.Pdf.GenerateScenarioMaintenanceDocumentPdf(UserId, MaintenanceId, TemplateId, OrganizationId);
        var word = await fixture.Word.GenerateScenarioMaintenanceDocumentWord(UserId, MaintenanceId, TemplateId, OrganizationId);

        Assert.Equal(DocumentExportOutcome.NotFound, pdf.Outcome);
        Assert.Equal(DocumentExportOutcome.NotFound, word.Outcome);
        fixture.MaintenanceQueryService.Verify(service => service.GetById(
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>()), Times.Never);
        fixture.VerifyNoGenerationOrHistory();
    }

    [Theory]
    [InlineData("pdf-vehicle")]
    [InlineData("pdf-maintenance")]
    [InlineData("pdf-intake")]
    [InlineData("pdf-act")]
    [InlineData("pdf-work-order")]
    [InlineData("pdf-scenario")]
    [InlineData("word-vehicle")]
    [InlineData("word-maintenance")]
    [InlineData("word-act")]
    [InlineData("word-work-order")]
    [InlineData("word-scenario")]
    public async Task OrganizationFeatureDenialPrecedesDocumentLookups(string operation)
    {
        var fixture = new DocumentExportTestFixture();
        fixture.OrganizationAccess.Setup(service => service.HasFeatureAsync(UserId, OrganizationId, It.IsAny<string>()))
            .ReturnsAsync(false);

        var result = await ExecuteOperation(fixture, operation);

        Assert.Equal(DocumentExportOutcome.AccessDenied, result.Outcome);
        fixture.OrganizationAccess.Verify(service => service.EnsureWorkspaceCanReadAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
        fixture.VehicleService.Verify(service => service.GetById(
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>()), Times.Never);
        fixture.MaintenanceQueryService.Verify(service => service.GetById(
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>()), Times.Never);
        fixture.VerifyNoGenerationOrHistory();
    }

    [Theory]
    [InlineData("pdf-vehicle")]
    [InlineData("pdf-maintenance")]
    [InlineData("pdf-intake")]
    [InlineData("pdf-act")]
    [InlineData("pdf-work-order")]
    [InlineData("pdf-scenario")]
    [InlineData("word-vehicle")]
    [InlineData("word-maintenance")]
    [InlineData("word-act")]
    [InlineData("word-work-order")]
    [InlineData("word-scenario")]
    public async Task WorkspacePermissionFailureDeniesEveryOperationBeforeDataRead(string operation)
    {
        var fixture = new DocumentExportTestFixture();
        fixture.OrganizationAccess
            .Setup(service => service.EnsureWorkspaceCanReadAsync(UserId, OrganizationId, OrganizationPermissionCodes.DocumentsExport))
            .ThrowsAsync(new UnauthorizedAccessException());

        var result = await ExecuteOperation(fixture, operation);

        Assert.Equal(DocumentExportOutcome.AccessDenied, result.Outcome);
        if (operation == "pdf-intake")
        {
            fixture.OrganizationAccess.Verify(service => service.EnsureWorkspaceCanReadAsync(
                UserId, OrganizationId, OrganizationPermissionCodes.DocumentsExport), Times.Exactly(2));
        }
        else
        {
            fixture.OrganizationAccess.Verify(service => service.EnsureWorkspaceCanReadAsync(
                UserId, OrganizationId, OrganizationPermissionCodes.DocumentsExport), Times.Once);
        }

        fixture.VehicleService.Verify(service => service.GetById(
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>()), Times.Never);
        fixture.MaintenanceQueryService.Verify(service => service.GetById(
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>()), Times.Never);
        fixture.VerifyNoGenerationOrHistory();
    }

    [Theory]
    [InlineData("pdf", "vehicle")]
    [InlineData("word", "vehicle")]
    [InlineData("pdf", "maintenance")]
    [InlineData("word", "maintenance")]
    public async Task PersonalExportUsesPersonalFeatureAccessAndScope(string format, string documentType)
    {
        const string personalUserId = "personal-document-user";
        var fixture = new DocumentExportTestFixture();
        fixture.Features.Setup(service => service.HasFeatureAsync(personalUserId, It.IsAny<string>())).ReturnsAsync(true);
        fixture.VehicleService.Setup(service => service.GetById(personalUserId, null, VehicleId)).ReturnsAsync(fixture.Vehicle);
        fixture.MaintenanceQueryService.Setup(service => service.Get(personalUserId, null, VehicleId)).ReturnsAsync(fixture.VehicleHistory);
        fixture.MaintenanceQueryService.Setup(service => service.GetById(personalUserId, null, MaintenanceId)).ReturnsAsync(fixture.Maintenance);

        var result = (format, documentType) switch
        {
            ("pdf", "vehicle") => await fixture.Pdf.GenerateVehiclePdf(personalUserId, VehicleId),
            ("word", "vehicle") => await fixture.Word.GenerateVehicleWord(personalUserId, VehicleId),
            ("pdf", "maintenance") => await fixture.Pdf.GenerateMaintenancePdf(personalUserId, MaintenanceId),
            ("word", "maintenance") => await fixture.Word.GenerateMaintenanceWord(personalUserId, MaintenanceId),
            _ => throw new ArgumentOutOfRangeException(nameof(documentType), documentType, "Неизвестный вид документа.")
        };

        Assert.Equal(DocumentExportOutcome.File, result.Outcome);
        var feature = format == "pdf" ? FeatureCodes.ExportPdf : FeatureCodes.ExportWord;
        fixture.Features.Verify(service => service.HasFeatureAsync(personalUserId, feature), Times.Once);
        fixture.VehicleService.Verify(service => service.GetById(personalUserId, null, VehicleId), Times.Once);
        fixture.MaintenanceQueryService.Verify(service => service.GetById(personalUserId, null, MaintenanceId),
            documentType == "maintenance" ? Times.Once : Times.Never);
        fixture.OrganizationAccess.Verify(service => service.GetWorkspaceAccessAsync(
            It.IsAny<string>(), It.IsAny<Guid>()), Times.Never);
        fixture.OrganizationAccess.Verify(service => service.EnsureWorkspaceCanReadAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData("pdf", "vehicle")]
    [InlineData("word", "vehicle")]
    [InlineData("pdf", "maintenance")]
    [InlineData("word", "maintenance")]
    public async Task PersonalFeatureDenialPrecedesDataRead(string format, string documentType)
    {
        const string personalUserId = "personal-document-user";
        var fixture = new DocumentExportTestFixture();
        fixture.Features.Setup(service => service.HasFeatureAsync(personalUserId, It.IsAny<string>())).ReturnsAsync(false);

        var result = (format, documentType) switch
        {
            ("pdf", "vehicle") => await fixture.Pdf.GenerateVehiclePdf(personalUserId, VehicleId),
            ("word", "vehicle") => await fixture.Word.GenerateVehicleWord(personalUserId, VehicleId),
            ("pdf", "maintenance") => await fixture.Pdf.GenerateMaintenancePdf(personalUserId, MaintenanceId),
            ("word", "maintenance") => await fixture.Word.GenerateMaintenanceWord(personalUserId, MaintenanceId),
            _ => throw new ArgumentOutOfRangeException(nameof(documentType), documentType, "Неизвестный вид документа.")
        };

        Assert.Equal(DocumentExportOutcome.AccessDenied, result.Outcome);
        fixture.VehicleService.Verify(service => service.GetById(
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>()), Times.Never);
        fixture.MaintenanceQueryService.Verify(service => service.GetById(
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>()), Times.Never);
        fixture.VerifyNoGenerationOrHistory();
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData("word")]
    public async Task WorkOrderFallsBackToMaintenanceDateWhenPlannedDateIsMissing(string format)
    {
        var fixture = new DocumentExportTestFixture();
        fixture.Maintenance.PlannedDate = null;
        fixture.Maintenance.PlannedStartTime = null;
        fixture.Maintenance.PlannedEndDate = null;
        fixture.Maintenance.PlannedEndTime = null;
        MaintenanceWorkActPdfModel? pdfModel = null;
        WordMaintenanceWorkActModel? wordModel = null;
        fixture.PdfGenerator.Setup(generator => generator.GenerateMaintenanceWorkOrderPdfAsync(It.IsAny<MaintenanceWorkActPdfModel>()))
            .Callback<MaintenanceWorkActPdfModel>(model => pdfModel = model).ReturnsAsync(FileBytes);
        fixture.DocxGenerator.Setup(generator => generator.GenerateMaintenanceWorkOrderDocxAsync(It.IsAny<WordMaintenanceWorkActModel>()))
            .Callback<WordMaintenanceWorkActModel>(model => wordModel = model).ReturnsAsync(FileBytes);

        var result = format == "pdf"
            ? await fixture.Pdf.GenerateMaintenanceWorkOrderPdf(UserId, MaintenanceId, OrganizationId)
            : await fixture.Word.GenerateMaintenanceWorkOrderWord(UserId, MaintenanceId, OrganizationId);

        Assert.Equal(DocumentExportOutcome.File, result.Outcome);
        Assert.Equal("03.02.2025", format == "pdf" ? pdfModel!.ActDate : wordModel!.ActDate);
        Assert.Null(format == "pdf" ? pdfModel!.PlannedPeriod : wordModel!.PlannedPeriod);
    }

    [Fact]
    public async Task IntakePrintRequiresRecordedAcceptanceBeforeVehicleOrRendering()
    {
        var fixture = new DocumentExportTestFixture();
        fixture.Maintenance.IntakeRecordedAt = null;

        var result = await fixture.Pdf.PrintMaintenanceIntake(UserId, MaintenanceId, OrganizationId);

        Assert.Equal(DocumentExportOutcome.NotFound, result.Outcome);
        fixture.VehicleService.Verify(service => service.GetById(
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>()), Times.Never);
        fixture.PdfGenerator.Verify(generator => generator.GenerateMaintenanceIntakePdfAsync(
            It.IsAny<MaintenanceIntakePdfModel>()), Times.Never);
        fixture.MaintenanceDocumentService.Verify(service => service.TrackDocumentGenerated(
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task MissingVehicleStopsMaintenanceExportsAfterMaintenanceLookup()
    {
        var fixture = new DocumentExportTestFixture();
        fixture.VehicleService.Setup(service => service.GetById(UserId, OrganizationId, VehicleId))
            .ReturnsAsync((GetVehicleDto)null!);

        var pdf = await fixture.Pdf.GenerateMaintenancePdf(UserId, MaintenanceId, organizationId: OrganizationId);
        var word = await fixture.Word.GenerateMaintenanceWord(UserId, MaintenanceId, organizationId: OrganizationId);

        Assert.Equal(DocumentExportOutcome.NotFound, pdf.Outcome);
        Assert.Equal(DocumentExportOutcome.NotFound, word.Outcome);
        fixture.MaintenanceQueryService.Verify(service => service.GetById(UserId, OrganizationId, MaintenanceId), Times.Exactly(2));
        fixture.VerifyNoGenerationOrHistory();
    }

    [Fact]
    public async Task ScenarioExportsRejectMissingAndForeignTemplatesBeforeMaintenanceLookup()
    {
        var missingFixture = new DocumentExportTestFixture();
        missingFixture.SetScenarioDocuments();
        var missingPdf = await missingFixture.Pdf.GenerateScenarioMaintenanceDocumentPdf(UserId, MaintenanceId, TemplateId, OrganizationId);
        var missingWord = await missingFixture.Word.GenerateScenarioMaintenanceDocumentWord(UserId, MaintenanceId, TemplateId, OrganizationId);

        var foreignFixture = new DocumentExportTestFixture();
        foreignFixture.SetScenarioDocuments(new OrganizationScenarioCatalogItemDto
        {
            Id = Guid.NewGuid(),
            Name = "Чужой шаблон",
            DocumentKindCode = OrganizationScenarioDocumentKindCodes.WorkOrder,
            IsActive = true
        });
        var foreignPdf = await foreignFixture.Pdf.GenerateScenarioMaintenanceDocumentPdf(UserId, MaintenanceId, TemplateId, OrganizationId);
        var foreignWord = await foreignFixture.Word.GenerateScenarioMaintenanceDocumentWord(UserId, MaintenanceId, TemplateId, OrganizationId);

        Assert.Equal(DocumentExportOutcome.NotFound, missingPdf.Outcome);
        Assert.Equal(DocumentExportOutcome.NotFound, missingWord.Outcome);
        Assert.Equal(DocumentExportOutcome.NotFound, foreignPdf.Outcome);
        Assert.Equal(DocumentExportOutcome.NotFound, foreignWord.Outcome);
        missingFixture.MaintenanceQueryService.Verify(service => service.GetById(
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>()), Times.Never);
        foreignFixture.MaintenanceQueryService.Verify(service => service.GetById(
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>()), Times.Never);
        missingFixture.VerifyNoGenerationOrHistory();
        foreignFixture.VerifyNoGenerationOrHistory();
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData("word")]
    public async Task UnknownScenarioKindFallsBackToMaintenanceReportWithoutPhotos(string format)
    {
        var fixture = new DocumentExportTestFixture();
        fixture.SetTemplate("future-document-kind", "Неизвестный шаблон");
        var photoId = Guid.NewGuid();
        fixture.Maintenance.PhotoIds = [photoId];
        ScenarioMaintenanceDocumentPdfModel? pdfModel = null;
        WordScenarioMaintenanceDocumentModel? wordModel = null;
        fixture.PdfGenerator.Setup(generator => generator.GenerateScenarioMaintenanceDocumentPdfAsync(It.IsAny<ScenarioMaintenanceDocumentPdfModel>()))
            .Callback<ScenarioMaintenanceDocumentPdfModel>(model => pdfModel = model).ReturnsAsync(FileBytes);
        fixture.DocxGenerator.Setup(generator => generator.GenerateScenarioMaintenanceDocumentDocxAsync(It.IsAny<WordScenarioMaintenanceDocumentModel>()))
            .Callback<WordScenarioMaintenanceDocumentModel>(model => wordModel = model).ReturnsAsync(FileBytes);

        var result = format == "pdf"
            ? await fixture.Pdf.GenerateScenarioMaintenanceDocumentPdf(UserId, MaintenanceId, TemplateId, OrganizationId)
            : await fixture.Word.GenerateScenarioMaintenanceDocumentWord(UserId, MaintenanceId, TemplateId, OrganizationId);

        Assert.Equal(DocumentExportOutcome.File, result.Outcome);
        Assert.Equal(OrganizationScenarioDocumentKindCodes.MaintenanceReport,
            format == "pdf" ? pdfModel!.DocumentKindCode : wordModel!.DocumentKindCode);
        if (format == "pdf")
        {
            Assert.Empty(pdfModel!.Photos);
        }
        else
        {
            Assert.Empty(wordModel!.Photos);
        }

        fixture.Media.Verify(service => service.UserHasAccessToMediaAsync(UserId, photoId), Times.Never);
    }

    [Theory]
    [InlineData("pdf-vehicle")]
    [InlineData("pdf-maintenance")]
    [InlineData("pdf-intake")]
    [InlineData("pdf-act")]
    [InlineData("pdf-work-order")]
    [InlineData("pdf-scenario")]
    [InlineData("word-vehicle")]
    [InlineData("word-maintenance")]
    [InlineData("word-act")]
    [InlineData("word-work-order")]
    [InlineData("word-scenario")]
    public async Task RendererFailureNeverRecordsHistoryForAnyOperation(string operation)
    {
        var fixture = new DocumentExportTestFixture();
        fixture.Maintenance.Status = MaintenanceStatus.Completed;
        ConfigureRendererFailure(fixture, operation);

        await Assert.ThrowsAsync<InvalidOperationException>(() => ExecuteOperation(fixture, operation));

        fixture.VehicleService.As<IVehicleDocumentHistoryService>().Verify(service => service.TrackDocumentGenerated(
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
        fixture.MaintenanceDocumentService.Verify(service => service.TrackDocumentGenerated(
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData("word")]
    public async Task MissingPhotoFileIsSkippedAndOpenedPhotoStreamIsDisposedAfterReadFailure(string format)
    {
        var fixture = new DocumentExportTestFixture();
        var missingPhotoId = Guid.NewGuid();
        var failingPhotoId = Guid.NewGuid();
        fixture.SetTemplate(OrganizationScenarioDocumentKindCodes.PhotoReport, "Фотоотчёт");
        fixture.Maintenance.PhotoIds = [missingPhotoId, failingPhotoId];
        fixture.SetMaintenancePhotos(
            new MaintenancePhotoDto(missingPhotoId, "", MaintenancePhotoStage.Before, new DateTime(2025, 2, 3)),
            new MaintenancePhotoDto(failingPhotoId, "", MaintenancePhotoStage.After, new DateTime(2025, 2, 4)));
        fixture.AllowPhotoAccess();
        fixture.Media.Setup(service => service.GetMediaPathAsync(UserId, missingPhotoId)).ReturnsAsync($"documents/{missingPhotoId:N}.jpg");
        fixture.FileStorage.Setup(service => service.FileExistsAsync($"documents/{missingPhotoId:N}.jpg")).ReturnsAsync(false);
        var failingStream = fixture.ConfigurePhotoReadFailure(failingPhotoId);
        ScenarioMaintenanceDocumentPdfModel? pdfModel = null;
        WordScenarioMaintenanceDocumentModel? wordModel = null;
        fixture.PdfGenerator.Setup(generator => generator.GenerateScenarioMaintenanceDocumentPdfAsync(It.IsAny<ScenarioMaintenanceDocumentPdfModel>()))
            .Callback<ScenarioMaintenanceDocumentPdfModel>(model => pdfModel = model).ReturnsAsync(FileBytes);
        fixture.DocxGenerator.Setup(generator => generator.GenerateScenarioMaintenanceDocumentDocxAsync(It.IsAny<WordScenarioMaintenanceDocumentModel>()))
            .Callback<WordScenarioMaintenanceDocumentModel>(model => wordModel = model).ReturnsAsync(FileBytes);

        var result = format == "pdf"
            ? await fixture.Pdf.GenerateScenarioMaintenanceDocumentPdf(UserId, MaintenanceId, TemplateId, OrganizationId)
            : await fixture.Word.GenerateScenarioMaintenanceDocumentWord(UserId, MaintenanceId, TemplateId, OrganizationId);

        Assert.Equal(DocumentExportOutcome.File, result.Outcome);
        if (format == "pdf")
        {
            Assert.Empty(pdfModel!.Photos);
        }
        else
        {
            Assert.Empty(wordModel!.Photos);
        }

        Assert.True(failingStream.IsDisposed);
    }

    [Theory]
    [InlineData("pdf", "path")]
    [InlineData("pdf", "open")]
    [InlineData("word", "path")]
    [InlineData("word", "open")]
    public async Task PhotoPathAndStorageFailuresAreSkipped(string format, string failureStage)
    {
        var fixture = new DocumentExportTestFixture();
        var photoId = Guid.NewGuid();
        fixture.SetTemplate(OrganizationScenarioDocumentKindCodes.PhotoReport, "Фотоотчёт");
        fixture.Maintenance.PhotoIds = [photoId];
        fixture.SetMaintenancePhotos(new MaintenancePhotoDto(
            photoId, "", MaintenancePhotoStage.Before, new DateTime(2025, 2, 3)));
        fixture.AllowPhotoAccess();
        if (failureStage == "path")
        {
            fixture.ConfigurePhotoPathFailure(photoId);
        }
        else
        {
            fixture.ConfigurePhotoOpenFailure(photoId);
        }

        ScenarioMaintenanceDocumentPdfModel? pdfModel = null;
        WordScenarioMaintenanceDocumentModel? wordModel = null;
        fixture.PdfGenerator.Setup(generator => generator.GenerateScenarioMaintenanceDocumentPdfAsync(It.IsAny<ScenarioMaintenanceDocumentPdfModel>()))
            .Callback<ScenarioMaintenanceDocumentPdfModel>(model => pdfModel = model).ReturnsAsync(FileBytes);
        fixture.DocxGenerator.Setup(generator => generator.GenerateScenarioMaintenanceDocumentDocxAsync(It.IsAny<WordScenarioMaintenanceDocumentModel>()))
            .Callback<WordScenarioMaintenanceDocumentModel>(model => wordModel = model).ReturnsAsync(FileBytes);

        var result = format == "pdf"
            ? await fixture.Pdf.GenerateScenarioMaintenanceDocumentPdf(UserId, MaintenanceId, TemplateId, OrganizationId)
            : await fixture.Word.GenerateScenarioMaintenanceDocumentWord(UserId, MaintenanceId, TemplateId, OrganizationId);

        Assert.Equal(DocumentExportOutcome.File, result.Outcome);
        if (format == "pdf")
        {
            Assert.Empty(pdfModel!.Photos);
        }
        else
        {
            Assert.Empty(wordModel!.Photos);
        }

        if (failureStage == "path")
        {
            fixture.FileStorage.Verify(service => service.FileExistsAsync(It.IsAny<string>()), Times.Never);
        }
        else
        {
            fixture.FileStorage.Verify(service => service.GetFileAsync($"documents/{photoId:N}.jpg"), Times.Once);
        }
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData("word")]
    public async Task PhotoWithEmptyResolvedPathIsSkipped(string format)
    {
        var fixture = new DocumentExportTestFixture();
        var photoId = Guid.NewGuid();
        fixture.SetTemplate(OrganizationScenarioDocumentKindCodes.PhotoReport, "Фотоотчёт");
        fixture.Maintenance.PhotoIds = [photoId];
        fixture.SetMaintenancePhotos(new MaintenancePhotoDto(
            photoId, "", MaintenancePhotoStage.Before, new DateTime(2025, 2, 3)));
        fixture.AllowPhotoAccess();
        fixture.ConfigureEmptyPhotoPath(photoId);
        ScenarioMaintenanceDocumentPdfModel? pdfModel = null;
        WordScenarioMaintenanceDocumentModel? wordModel = null;
        fixture.PdfGenerator.Setup(generator => generator.GenerateScenarioMaintenanceDocumentPdfAsync(It.IsAny<ScenarioMaintenanceDocumentPdfModel>()))
            .Callback<ScenarioMaintenanceDocumentPdfModel>(model => pdfModel = model).ReturnsAsync(FileBytes);
        fixture.DocxGenerator.Setup(generator => generator.GenerateScenarioMaintenanceDocumentDocxAsync(It.IsAny<WordScenarioMaintenanceDocumentModel>()))
            .Callback<WordScenarioMaintenanceDocumentModel>(model => wordModel = model).ReturnsAsync(FileBytes);

        var result = format == "pdf"
            ? await fixture.Pdf.GenerateScenarioMaintenanceDocumentPdf(UserId, MaintenanceId, TemplateId, OrganizationId)
            : await fixture.Word.GenerateScenarioMaintenanceDocumentWord(UserId, MaintenanceId, TemplateId, OrganizationId);

        Assert.Equal(DocumentExportOutcome.File, result.Outcome);
        if (format == "pdf")
        {
            Assert.Empty(pdfModel!.Photos);
        }
        else
        {
            Assert.Empty(wordModel!.Photos);
        }

        fixture.FileStorage.Verify(service => service.FileExistsAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task IntakePrintSkipsBeforePhotoWithoutImageData()
    {
        var fixture = new DocumentExportTestFixture();
        var photoId = Guid.NewGuid();
        fixture.AllowPhotoAccess();
        fixture.SetMaintenancePhotos(new MaintenancePhotoDto(
            photoId, "", MaintenancePhotoStage.Before, new DateTime(2025, 2, 3)));
        fixture.Media.Setup(service => service.UserHasAccessToMediaAsync(UserId, photoId)).ReturnsAsync(false);
        MaintenanceIntakePdfModel? generatedModel = null;
        fixture.PdfGenerator.Setup(generator => generator.GenerateMaintenanceIntakePdfAsync(It.IsAny<MaintenanceIntakePdfModel>()))
            .Callback<MaintenanceIntakePdfModel>(model => generatedModel = model).ReturnsAsync(FileBytes);

        var result = await fixture.Pdf.PrintMaintenanceIntake(UserId, MaintenanceId, OrganizationId);

        Assert.Equal(DocumentExportOutcome.File, result.Outcome);
        Assert.Empty(generatedModel!.BeforePhotos);
        fixture.Media.Verify(service => service.GetMediaPathAsync(UserId, photoId), Times.Never);
        fixture.MaintenanceDocumentService.Verify(service => service.TrackDocumentGenerated(
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Theory]
    [InlineData("pdf-act")]
    [InlineData("pdf-work-order")]
    [InlineData("pdf-scenario")]
    [InlineData("word-act")]
    [InlineData("word-work-order")]
    [InlineData("word-scenario")]
    public async Task DocumentUsesOrganizationLegalNameWhenItIsPresent(string operation)
    {
        var fixture = new DocumentExportTestFixture();
        fixture.Maintenance.Status = MaintenanceStatus.Completed;
        fixture.OrganizationDetails.LegalName = "ООО Юридическое лицо";
        fixture.OrganizationDetails.LegalAddress = null;
        fixture.OrganizationDetails.SignatoryPosition = "Директор";
        fixture.OrganizationDetails.SignatoryName = "Иванов Иван";
        fixture.OrganizationDetails.SignatoryBasis = "По доверенности";
        fixture.Vehicle.MileageUnit = VehicleMileageUnit.EngineHours;
        fixture.Vehicle.YearIssue = null;
        fixture.Vehicle.OrganizationClientName = null;
        string? executorName = null;
        string? vehicleYear = "not-null";
        string? executorAddress = null;
        string? signatoryPosition = null;
        string? signatoryName = null;
        string? customerName = null;
        string? signatoryBasis = null;
        ConfigureDocumentMetadataCapture(fixture, operation, (name, year, address, position, signer, customer, basis) =>
        {
            executorName = name;
            vehicleYear = year;
            executorAddress = address;
            signatoryPosition = position;
            signatoryName = signer;
            customerName = customer;
            signatoryBasis = basis;
        });

        var result = await ExecuteOperation(fixture, operation);

        Assert.Equal(DocumentExportOutcome.File, result.Outcome);
        Assert.Equal("ООО Юридическое лицо", executorName);
        Assert.Null(vehicleYear);
        Assert.Equal("Фактический адрес", executorAddress);
        Assert.Equal("Директор", signatoryPosition);
        Assert.Equal("Иванов Иван", signatoryName);
        Assert.Equal(string.Empty, customerName);
        Assert.Equal(operation.EndsWith("scenario", StringComparison.Ordinal) ? null : "По доверенности", signatoryBasis);
    }

    [Theory]
    [InlineData("pdf-act")]
    [InlineData("word-act")]
    public async Task WorkActVehicleTitleOmitsBlankVehicleName(string operation)
    {
        var fixture = new DocumentExportTestFixture();
        fixture.Maintenance.Status = MaintenanceStatus.Completed;
        fixture.Vehicle.Name = "  ";
        string? vehicleTitle = null;
        if (operation == "pdf-act")
        {
            fixture.PdfGenerator.Setup(generator => generator.GenerateMaintenanceWorkActPdfAsync(It.IsAny<MaintenanceWorkActPdfModel>()))
                .Callback<MaintenanceWorkActPdfModel>(model => vehicleTitle = model.VehicleTitle)
                .ReturnsAsync(FileBytes);
        }
        else
        {
            fixture.DocxGenerator.Setup(generator => generator.GenerateMaintenanceWorkActDocxAsync(It.IsAny<WordMaintenanceWorkActModel>()))
                .Callback<WordMaintenanceWorkActModel>(model => vehicleTitle = model.VehicleTitle)
                .ReturnsAsync(FileBytes);
        }

        var result = await ExecuteOperation(fixture, operation);

        Assert.Equal(DocumentExportOutcome.File, result.Outcome);
        Assert.Equal("Мастерская Грузовик", vehicleTitle);
    }

    [Theory]
    [InlineData("pdf", 1, "Ремонт и обслуживание")]
    [InlineData("pdf", 3, "Заправка топливом")]
    [InlineData("pdf", 100, "Другое")]
    [InlineData("pdf", 999, "Обслуживание")]
    [InlineData("word", 1, "Ремонт и обслуживание")]
    [InlineData("word", 3, "Заправка топливом")]
    [InlineData("word", 100, "Другое")]
    [InlineData("word", 999, "Обслуживание")]
    public async Task WorkActMapsEveryMaintenanceType(string format, int type, string expectedType)
    {
        var fixture = new DocumentExportTestFixture();
        fixture.Maintenance.Status = MaintenanceStatus.Completed;
        fixture.Maintenance.Type = (MaintenanceType)type;
        string? mappedType = null;
        fixture.PdfGenerator.Setup(generator => generator.GenerateMaintenanceWorkActPdfAsync(It.IsAny<MaintenanceWorkActPdfModel>()))
            .Callback<MaintenanceWorkActPdfModel>(model => mappedType = model.MaintenanceType).ReturnsAsync(FileBytes);
        fixture.DocxGenerator.Setup(generator => generator.GenerateMaintenanceWorkActDocxAsync(It.IsAny<WordMaintenanceWorkActModel>()))
            .Callback<WordMaintenanceWorkActModel>(model => mappedType = model.MaintenanceType).ReturnsAsync(FileBytes);

        var result = format == "pdf"
            ? await fixture.Pdf.GenerateMaintenanceWorkActPdf(UserId, MaintenanceId, OrganizationId)
            : await fixture.Word.GenerateMaintenanceWorkActWord(UserId, MaintenanceId, OrganizationId);

        Assert.Equal(DocumentExportOutcome.File, result.Outcome);
        Assert.Equal(expectedType, mappedType);
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData("word")]
    public async Task ScenarioFileNameFallsBackWhenTemplateNameSanitizesToEmpty(string format)
    {
        var fixture = new DocumentExportTestFixture();
        fixture.SetTemplate(OrganizationScenarioDocumentKindCodes.WorkOrder, " / ");

        var result = format == "pdf"
            ? await fixture.Pdf.GenerateScenarioMaintenanceDocumentPdf(UserId, MaintenanceId, TemplateId, OrganizationId)
            : await fixture.Word.GenerateScenarioMaintenanceDocumentWord(UserId, MaintenanceId, TemplateId, OrganizationId);

        Assert.Equal(DocumentExportOutcome.File, result.Outcome);
        Assert.StartsWith("document_", result.FileName);
    }

    [Theory]
    [InlineData("word-vehicle")]
    [InlineData("word-maintenance")]
    [InlineData("word-act")]
    [InlineData("word-work-order")]
    [InlineData("word-scenario")]
    public async Task MaintenanceDetailsPreserveNullPricesInWordModels(string operation)
    {
        var fixture = new DocumentExportTestFixture();
        decimal? mappedPrice = 1;
        if (operation == "word-vehicle")
        {
            var history = DocumentExportTestFixture.CreateHistory(MaintenanceId, "Услуга", 0, new DateTime(2025, 2, 3));
            history.MaintenanceDetails.Single().Price = null;
            fixture.AddVehicleHistory(history);
            fixture.DocxGenerator.Setup(generator => generator.GenerateVehicleHistoryDocxAsync(It.IsAny<WordVehicleModel>(), It.IsAny<bool>()))
                .Callback<WordVehicleModel, bool>((model, _) => mappedPrice = model.MaintenanceHistory.Single().MaintenanceDetails.Single().Price)
                .ReturnsAsync(FileBytes);
        }
        else
        {
            fixture.Maintenance.MaintenanceDetails.Single().Price = null;
            fixture.Maintenance.Status = MaintenanceStatus.Completed;
            fixture.DocxGenerator.Setup(generator => generator.GenerateMaintenanceDocxAsync(It.IsAny<WordMaintenanceModel>(), It.IsAny<bool>()))
                .Callback<WordMaintenanceModel, bool>((model, _) => mappedPrice = model.Maintenance.MaintenanceDetails.Single().Price)
                .ReturnsAsync(FileBytes);
            fixture.DocxGenerator.Setup(generator => generator.GenerateMaintenanceWorkActDocxAsync(It.IsAny<WordMaintenanceWorkActModel>()))
                .Callback<WordMaintenanceWorkActModel>(model => mappedPrice = model.Works.Single().Price)
                .ReturnsAsync(FileBytes);
            fixture.DocxGenerator.Setup(generator => generator.GenerateMaintenanceWorkOrderDocxAsync(It.IsAny<WordMaintenanceWorkActModel>()))
                .Callback<WordMaintenanceWorkActModel>(model => mappedPrice = model.Works.Single().Price)
                .ReturnsAsync(FileBytes);
            fixture.DocxGenerator.Setup(generator => generator.GenerateScenarioMaintenanceDocumentDocxAsync(It.IsAny<WordScenarioMaintenanceDocumentModel>()))
                .Callback<WordScenarioMaintenanceDocumentModel>(model => mappedPrice = model.Works.Single().Price)
                .ReturnsAsync(FileBytes);
        }

        var result = await ExecuteOperation(fixture, operation);

        Assert.Equal(DocumentExportOutcome.File, result.Outcome);
        Assert.Null(mappedPrice);
    }

    [Fact]
    public async Task WordMaintenanceWithPhotoEntitlementOmitsMissingVehiclePhoto()
    {
        var fixture = new DocumentExportTestFixture();
        fixture.VehiclePhotoId = null;
        WordMaintenanceModel? captured = null;
        fixture.DocxGenerator.Setup(generator => generator.GenerateMaintenanceDocxAsync(It.IsAny<WordMaintenanceModel>(), true))
            .Callback<WordMaintenanceModel, bool>((model, _) => captured = model)
            .ReturnsAsync(FileBytes);

        var result = await fixture.Word.GenerateMaintenanceWord(UserId, MaintenanceId, true, OrganizationId);

        Assert.Equal(DocumentExportOutcome.File, result.Outcome);
        Assert.Null(captured!.Vehicle.PhotoData);
        fixture.Media.Verify(service => service.GetMediaPathAsync(It.IsAny<string>(), It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task WordScenarioUsesMaintenanceDateWhenCompletionAndPlanAreMissing()
    {
        var fixture = new DocumentExportTestFixture();
        fixture.Maintenance.CompletedDate = null;
        fixture.Maintenance.PlannedDate = null;
        WordScenarioMaintenanceDocumentModel? captured = null;
        fixture.DocxGenerator.Setup(generator => generator.GenerateScenarioMaintenanceDocumentDocxAsync(It.IsAny<WordScenarioMaintenanceDocumentModel>()))
            .Callback<WordScenarioMaintenanceDocumentModel>(model => captured = model)
            .ReturnsAsync(FileBytes);

        var result = await fixture.Word.GenerateScenarioMaintenanceDocumentWord(UserId, MaintenanceId, TemplateId, OrganizationId);

        Assert.Equal(DocumentExportOutcome.File, result.Outcome);
        Assert.Equal(fixture.Maintenance.Date.ToString("dd.MM.yyyy"), captured!.DocumentDate);
    }

    [Fact]
    public async Task NullableMaintenancePriceIsPreservedInWordModel()
    {
        var fixture = new DocumentExportTestFixture();
        fixture.Maintenance.Price = null;
        WordMaintenanceModel? captured = null;
        fixture.DocxGenerator.Setup(generator => generator.GenerateMaintenanceDocxAsync(It.IsAny<WordMaintenanceModel>(), false))
            .Callback<WordMaintenanceModel, bool>((model, _) => captured = model)
            .ReturnsAsync(FileBytes);

        var result = await fixture.Word.GenerateMaintenanceWord(UserId, MaintenanceId, false, OrganizationId);

        Assert.Equal(DocumentExportOutcome.File, result.Outcome);
        Assert.Null(captured!.Maintenance.Price);
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData("word")]
    public async Task PhotoEntitlementDenialReportsRequestedFormatBeforeAnyLookup(string format)
    {
        var fixture = new DocumentExportTestFixture();
        fixture.OrganizationAccess.Setup(service => service.HasFeatureAsync(UserId, OrganizationId, It.IsAny<string>()))
            .ReturnsAsync(false);

        var result = format == "pdf"
            ? await fixture.Pdf.GenerateMaintenancePdf(UserId, MaintenanceId, true, OrganizationId)
            : await fixture.Word.GenerateVehicleWord(UserId, VehicleId, true, OrganizationId);

        Assert.Equal(DocumentExportOutcome.AccessDenied, result.Outcome);
        Assert.Equal(format == "pdf" ? "У вас нет доступа к PDF-экспорту с фотографиями." : "У вас нет доступа к Word-экспорту с фотографиями.", result.Message);
        fixture.VerifyNoGenerationOrHistory();
    }

    private static void ConfigureDocumentMetadataCapture(
        DocumentExportTestFixture fixture,
        string operation,
        Action<string?, string?, string?, string?, string?, string?, string?> capture)
    {
        switch (operation)
        {
            case "pdf-work-order":
                fixture.PdfGenerator.Setup(generator => generator.GenerateMaintenanceWorkOrderPdfAsync(It.IsAny<MaintenanceWorkActPdfModel>()))
                    .Callback<MaintenanceWorkActPdfModel>(model =>
                    {
                        Assert.Equal("Моточасы", model.MileageLabel);
                        Assert.Equal("м/ч", model.MileageUnit);
                        capture(
                        model.ExecutorName, model.VehicleYear, model.ExecutorAddress, model.ExecutorSignatoryPosition,
                        model.ExecutorSignatoryName, model.CustomerName, model.ExecutorSignatoryBasis);
                    }).ReturnsAsync(FileBytes);
                break;
            case "pdf-act":
                fixture.PdfGenerator.Setup(generator => generator.GenerateMaintenanceWorkActPdfAsync(It.IsAny<MaintenanceWorkActPdfModel>()))
                    .Callback<MaintenanceWorkActPdfModel>(model =>
                    {
                        Assert.Equal("Моточасы", model.MileageLabel);
                        Assert.Equal("м/ч", model.MileageUnit);
                        capture(
                        model.ExecutorName, model.VehicleYear, model.ExecutorAddress, model.ExecutorSignatoryPosition,
                        model.ExecutorSignatoryName, model.CustomerName, model.ExecutorSignatoryBasis);
                    }).ReturnsAsync(FileBytes);
                break;
            case "pdf-scenario":
                fixture.PdfGenerator.Setup(generator => generator.GenerateScenarioMaintenanceDocumentPdfAsync(It.IsAny<ScenarioMaintenanceDocumentPdfModel>()))
                    .Callback<ScenarioMaintenanceDocumentPdfModel>(model =>
                    {
                        Assert.Equal("Моточасы", model.MileageLabel);
                        Assert.Equal("м/ч", model.MileageUnit);
                        capture(
                        model.ExecutorName, model.VehicleYear, model.ExecutorAddress, model.ExecutorSignatoryPosition,
                        model.ExecutorSignatoryName, model.CustomerName, null);
                    }).ReturnsAsync(FileBytes);
                break;
            case "word-act":
                fixture.DocxGenerator.Setup(generator => generator.GenerateMaintenanceWorkActDocxAsync(It.IsAny<WordMaintenanceWorkActModel>()))
                    .Callback<WordMaintenanceWorkActModel>(model =>
                    {
                        Assert.Equal("Моточасы", model.MileageLabel);
                        Assert.Equal("м/ч", model.MileageUnit);
                        capture(
                        model.ExecutorName, model.VehicleYear, model.ExecutorAddress, model.ExecutorSignatoryPosition,
                        model.ExecutorSignatoryName, model.CustomerName, model.ExecutorSignatoryBasis);
                    }).ReturnsAsync(FileBytes);
                break;
            case "word-work-order":
                fixture.DocxGenerator.Setup(generator => generator.GenerateMaintenanceWorkOrderDocxAsync(It.IsAny<WordMaintenanceWorkActModel>()))
                    .Callback<WordMaintenanceWorkActModel>(model =>
                    {
                        Assert.Equal("Моточасы", model.MileageLabel);
                        Assert.Equal("м/ч", model.MileageUnit);
                        capture(
                        model.ExecutorName, model.VehicleYear, model.ExecutorAddress, model.ExecutorSignatoryPosition,
                        model.ExecutorSignatoryName, model.CustomerName, model.ExecutorSignatoryBasis);
                    }).ReturnsAsync(FileBytes);
                break;
            case "word-scenario":
                fixture.DocxGenerator.Setup(generator => generator.GenerateScenarioMaintenanceDocumentDocxAsync(It.IsAny<WordScenarioMaintenanceDocumentModel>()))
                    .Callback<WordScenarioMaintenanceDocumentModel>(model =>
                    {
                        Assert.Equal("Моточасы", model.MileageLabel);
                        Assert.Equal("м/ч", model.MileageUnit);
                        capture(
                        model.ExecutorName, model.VehicleYear, model.ExecutorAddress, model.ExecutorSignatoryPosition,
                        model.ExecutorSignatoryName, model.CustomerName, null);
                    }).ReturnsAsync(FileBytes);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation, "Неизвестная операция экспорта.");
        }
    }

    private static void ConfigureRendererFailure(DocumentExportTestFixture fixture, string operation)
    {
        switch (operation)
        {
            case "pdf-vehicle":
                fixture.PdfGenerator.Setup(generator => generator.GenerateVehiclePdfAsync(It.IsAny<VehiclePdfModel>(), It.IsAny<bool>()))
                    .ThrowsAsync(new InvalidOperationException("PDF renderer failed"));
                break;
            case "pdf-maintenance":
                fixture.PdfGenerator.Setup(generator => generator.GenerateMaintenancePdfAsync(It.IsAny<MaintenancePdfModel>(), It.IsAny<bool>()))
                    .ThrowsAsync(new InvalidOperationException("PDF renderer failed"));
                break;
            case "pdf-intake":
                fixture.PdfGenerator.Setup(generator => generator.GenerateMaintenanceIntakePdfAsync(It.IsAny<MaintenanceIntakePdfModel>()))
                    .ThrowsAsync(new InvalidOperationException("PDF renderer failed"));
                break;
            case "pdf-act":
                fixture.PdfGenerator.Setup(generator => generator.GenerateMaintenanceWorkActPdfAsync(It.IsAny<MaintenanceWorkActPdfModel>()))
                    .ThrowsAsync(new InvalidOperationException("PDF renderer failed"));
                break;
            case "pdf-work-order":
                fixture.PdfGenerator.Setup(generator => generator.GenerateMaintenanceWorkOrderPdfAsync(It.IsAny<MaintenanceWorkActPdfModel>()))
                    .ThrowsAsync(new InvalidOperationException("PDF renderer failed"));
                break;
            case "pdf-scenario":
                fixture.PdfGenerator.Setup(generator => generator.GenerateScenarioMaintenanceDocumentPdfAsync(It.IsAny<ScenarioMaintenanceDocumentPdfModel>()))
                    .ThrowsAsync(new InvalidOperationException("PDF renderer failed"));
                break;
            case "word-vehicle":
                fixture.DocxGenerator.Setup(generator => generator.GenerateVehicleHistoryDocxAsync(It.IsAny<WordVehicleModel>(), It.IsAny<bool>()))
                    .ThrowsAsync(new InvalidOperationException("DOCX renderer failed"));
                break;
            case "word-maintenance":
                fixture.DocxGenerator.Setup(generator => generator.GenerateMaintenanceDocxAsync(It.IsAny<WordMaintenanceModel>(), It.IsAny<bool>()))
                    .ThrowsAsync(new InvalidOperationException("DOCX renderer failed"));
                break;
            case "word-act":
                fixture.DocxGenerator.Setup(generator => generator.GenerateMaintenanceWorkActDocxAsync(It.IsAny<WordMaintenanceWorkActModel>()))
                    .ThrowsAsync(new InvalidOperationException("DOCX renderer failed"));
                break;
            case "word-work-order":
                fixture.DocxGenerator.Setup(generator => generator.GenerateMaintenanceWorkOrderDocxAsync(It.IsAny<WordMaintenanceWorkActModel>()))
                    .ThrowsAsync(new InvalidOperationException("DOCX renderer failed"));
                break;
            case "word-scenario":
                fixture.DocxGenerator.Setup(generator => generator.GenerateScenarioMaintenanceDocumentDocxAsync(It.IsAny<WordScenarioMaintenanceDocumentModel>()))
                    .ThrowsAsync(new InvalidOperationException("DOCX renderer failed"));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation, "Неизвестная операция экспорта.");
        }
    }

    private static Task<DocumentExportResult> ExecuteOperation(DocumentExportTestFixture fixture, string operation)
    {
        return operation switch
        {
            "pdf-vehicle" => fixture.Pdf.GenerateVehiclePdf(UserId, VehicleId, organizationId: OrganizationId),
            "pdf-maintenance" => fixture.Pdf.GenerateMaintenancePdf(UserId, MaintenanceId, organizationId: OrganizationId),
            "pdf-intake" => fixture.Pdf.PrintMaintenanceIntake(UserId, MaintenanceId, OrganizationId),
            "pdf-act" => fixture.Pdf.GenerateMaintenanceWorkActPdf(UserId, MaintenanceId, OrganizationId),
            "pdf-work-order" => fixture.Pdf.GenerateMaintenanceWorkOrderPdf(UserId, MaintenanceId, OrganizationId),
            "pdf-scenario" => fixture.Pdf.GenerateScenarioMaintenanceDocumentPdf(UserId, MaintenanceId, TemplateId, OrganizationId),
            "word-vehicle" => fixture.Word.GenerateVehicleWord(UserId, VehicleId, organizationId: OrganizationId),
            "word-maintenance" => fixture.Word.GenerateMaintenanceWord(UserId, MaintenanceId, organizationId: OrganizationId),
            "word-act" => fixture.Word.GenerateMaintenanceWorkActWord(UserId, MaintenanceId, OrganizationId),
            "word-work-order" => fixture.Word.GenerateMaintenanceWorkOrderWord(UserId, MaintenanceId, OrganizationId),
            "word-scenario" => fixture.Word.GenerateScenarioMaintenanceDocumentWord(UserId, MaintenanceId, TemplateId, OrganizationId),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Неизвестная операция экспорта.")
        };
    }

    private static void AssertFileResult(DocumentExportResult result, string contentType, byte[] content, bool hasFileName = true)
    {
        Assert.Equal(DocumentExportOutcome.File, result.Outcome);
        Assert.Equal(contentType, result.ContentType);
        Assert.Equal(content, result.Content);
        Assert.Equal(hasFileName, result.FileName != null);
    }

    private sealed class CallbackEnumerable<T>(Action beforeEnumeration, IReadOnlyCollection<T> items) : IEnumerable<T>
    {
        public IEnumerator<T> GetEnumerator()
        {
            beforeEnumeration();

            return items.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
    }

    private sealed class ReadCallbackPhotoStream(byte[] imageData, Action afterRead) : MemoryStream(imageData)
    {
        public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            await base.CopyToAsync(destination, bufferSize, cancellationToken);
            afterRead();
        }
    }
}
