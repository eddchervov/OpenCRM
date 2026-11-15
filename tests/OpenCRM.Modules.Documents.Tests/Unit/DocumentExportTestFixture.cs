using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Catalog.Contracts.Dtos;
using OpenCRM.Modules.Catalog.Contracts;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Modules.Organizations.Contracts.Dtos;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Modules.Documents.Pdf;
using OpenCRM.Modules.Documents.Word;
using OpenCRM.Modules.Documents;
using OpenCRM.Services.Interfaces.Documents;
using OpenCRM.Services.Interfaces.Features;
using OpenCRM.Services.Interfaces.FileStorages;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Modules.Maintenance.Contracts.Photos;
using OpenCRM.Services.Interfaces.Media;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using OpenCRM.Services.Interfaces.Pdf;
using OpenCRM.Services.Interfaces.Pdf.Dtos;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Vehicles.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Word;
using OpenCRM.Services.Interfaces.Word.Dtos;
using Moq;

namespace OpenCRM.Modules.Documents.Tests.Unit;

internal sealed class DocumentExportTestFixture
{
    private static readonly byte[] s_defaultFileBytes = [1, 2, 3, 4];

    public DocumentExportTestFixture(bool exportsEnabled = true)
    {
        this.OrganizationAccess.Setup(service => service.GetWorkspaceAccessAsync(DocumentExportTests.UserId, DocumentExportTests.OrganizationId))
            .ReturnsAsync(new OrganizationWorkspaceAccessDto { ExportsEnabled = exportsEnabled });
        this.OrganizationAccess.Setup(service => service.HasFeatureAsync(
                DocumentExportTests.UserId, DocumentExportTests.OrganizationId, It.IsAny<string>()))
            .ReturnsAsync(true);
        this.OrganizationAccess.Setup(service => service.EnsureWorkspaceCanReadAsync(
                DocumentExportTests.UserId, DocumentExportTests.OrganizationId, It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        this.Features.Setup(service => service.HasFeatureAsync(DocumentExportTests.UserId, It.IsAny<string>()))
            .ReturnsAsync(true);
        this.VehicleService.Setup(service => service.GetById(
                DocumentExportTests.UserId, DocumentExportTests.OrganizationId, DocumentExportTests.VehicleId))
            .ReturnsAsync(this.Vehicle);
        this.VehicleService.Setup(service => service.GetById(
                DocumentExportTests.UserId, DocumentExportTests.OrganizationId, this.Maintenance.VehicleId))
            .ReturnsAsync(this.Vehicle);
        this.VehicleService.As<IVehicleDocumentHistoryService>().Setup(service => service.TrackDocumentGenerated(
                It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);
        this.MaintenanceQueryService.Setup(service => service.GetById(
                DocumentExportTests.UserId, DocumentExportTests.OrganizationId, DocumentExportTests.MaintenanceId))
            .ReturnsAsync(this.Maintenance);
        this.MaintenanceQueryService.Setup(service => service.Get(
                DocumentExportTests.UserId, DocumentExportTests.OrganizationId, DocumentExportTests.VehicleId))
            .ReturnsAsync(this.VehicleHistory);
        this.MaintenanceDocumentService.Setup(service => service.EnsureWorkActNumber(
                DocumentExportTests.UserId, DocumentExportTests.OrganizationId, DocumentExportTests.MaintenanceId))
            .ReturnsAsync(42);
        this.MaintenanceDocumentService.Setup(service => service.TrackDocumentGenerated(
                It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);
        this.Organization.Setup(service => service.GetByIdAsync(
                DocumentExportTests.UserId, DocumentExportTests.OrganizationId))
            .ReturnsAsync(this.OrganizationDetails);
        this.ScenarioCatalog.Setup(service => service.GetAsync(
                DocumentExportTests.UserId, DocumentExportTests.OrganizationId))
            .ReturnsAsync(new OrganizationScenarioCatalogDto
            {
                Documents = [this.Template]
            });
        this.MaintenancePhotos.Setup(service => service.GetMaintenancePhotosAsync(
                DocumentExportTests.UserId, DocumentExportTests.OrganizationId, DocumentExportTests.MaintenanceId))
            .ReturnsAsync(Array.Empty<MaintenancePhotoDto>());
        this.PdfGenerator.Setup(generator => generator.GenerateVehiclePdfAsync(It.IsAny<VehiclePdfModel>(), It.IsAny<bool>()))
            .ReturnsAsync(s_defaultFileBytes);
        this.PdfGenerator.Setup(generator => generator.GenerateMaintenancePdfAsync(It.IsAny<MaintenancePdfModel>(), It.IsAny<bool>()))
            .ReturnsAsync(s_defaultFileBytes);
        this.PdfGenerator.Setup(generator => generator.GenerateMaintenanceIntakePdfAsync(It.IsAny<MaintenanceIntakePdfModel>()))
            .ReturnsAsync(s_defaultFileBytes);
        this.PdfGenerator.Setup(generator => generator.GenerateMaintenanceWorkActPdfAsync(It.IsAny<MaintenanceWorkActPdfModel>()))
            .ReturnsAsync(s_defaultFileBytes);
        this.PdfGenerator.Setup(generator => generator.GenerateMaintenanceWorkOrderPdfAsync(It.IsAny<MaintenanceWorkActPdfModel>()))
            .ReturnsAsync(s_defaultFileBytes);
        this.PdfGenerator.Setup(generator => generator.GenerateScenarioMaintenanceDocumentPdfAsync(It.IsAny<ScenarioMaintenanceDocumentPdfModel>()))
            .ReturnsAsync(s_defaultFileBytes);
        this.DocxGenerator.Setup(generator => generator.GenerateVehicleHistoryDocxAsync(It.IsAny<WordVehicleModel>(), It.IsAny<bool>()))
            .ReturnsAsync(s_defaultFileBytes);
        this.DocxGenerator.Setup(generator => generator.GenerateMaintenanceDocxAsync(It.IsAny<WordMaintenanceModel>(), It.IsAny<bool>()))
            .ReturnsAsync(s_defaultFileBytes);
        this.DocxGenerator.Setup(generator => generator.GenerateMaintenanceWorkActDocxAsync(It.IsAny<WordMaintenanceWorkActModel>()))
            .ReturnsAsync(s_defaultFileBytes);
        this.DocxGenerator.Setup(generator => generator.GenerateMaintenanceWorkOrderDocxAsync(It.IsAny<WordMaintenanceWorkActModel>()))
            .ReturnsAsync(s_defaultFileBytes);
        this.DocxGenerator.Setup(generator => generator.GenerateScenarioMaintenanceDocumentDocxAsync(It.IsAny<WordScenarioMaintenanceDocumentModel>()))
            .ReturnsAsync(s_defaultFileBytes);

        this.IntakeDamages.Setup(service => service.GetDamages(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>()))
            .ReturnsAsync(new MaintenanceIntakeDamagesDto { DiagramVersion = "sedan-v1" });

        var access = new DocumentExportAccess(this.Features.Object, this.OrganizationAccess.Object,
            Mock.Of<IPlatformCommercialPolicy>(policy => policy.CommercialEnabled == true));
        var photoLoader = new DocumentPhotoLoader(this.MaintenancePhotos.Object, this.Media.Object, this.FileStorage.Object);
        var vehicleReportWorkflow = new VehicleReportWorkflow(
            this.PdfGenerator.Object,
            this.DocxGenerator.Object,
            this.VehicleService.As<IVehicleDocumentHistoryService>().Object,
            new VehicleDocumentDataLoader(this.VehicleService.Object, this.MaintenanceQueryService.Object),
            access,
            photoLoader);
        var maintenanceReportWorkflow = new MaintenanceReportWorkflow(
            this.PdfGenerator.Object,
            this.DocxGenerator.Object,
            this.MaintenanceDocumentService.Object,
            new MaintenanceReportDataLoader(this.MaintenanceQueryService.Object, this.VehicleService.Object),
            access,
            photoLoader);
        var workDocumentsWorkflow = new WorkDocumentsWorkflow(
            this.PdfGenerator.Object,
            this.DocxGenerator.Object,
            this.MaintenanceQueryService.Object,
            this.MaintenanceDocumentService.Object,
            this.VehicleService.Object,
            this.Organization.Object,
            access);
        var scenarioDocumentWorkflow = new ScenarioDocumentWorkflow(
            this.PdfGenerator.Object,
            this.DocxGenerator.Object,
            this.MaintenanceQueryService.Object,
            this.MaintenanceDocumentService.Object,
            this.VehicleService.Object,
            this.Organization.Object,
            this.ScenarioCatalog.Object,
            access,
            photoLoader);
        var maintenanceIntakeWorkflow = new MaintenanceIntakeWorkflow(
            this.PdfGenerator.Object,
            this.MaintenanceQueryService.Object,
            this.VehicleService.Object,
            this.Organization.Object,
            access,
            photoLoader,
            this.IntakeDamages.Object);
        this.Pdf = new PdfDocumentExportService(
            vehicleReportWorkflow,
            maintenanceReportWorkflow,
            workDocumentsWorkflow,
            scenarioDocumentWorkflow,
            maintenanceIntakeWorkflow);
        this.Word = new WordDocumentExportService(
            vehicleReportWorkflow,
            maintenanceReportWorkflow,
            workDocumentsWorkflow,
            scenarioDocumentWorkflow);
    }

    public Mock<IPdfGeneratorService> PdfGenerator { get; } = new();
    public Mock<IDocxGeneratorService> DocxGenerator { get; } = new();
    public Mock<IMaintenanceQueryService> MaintenanceQueryService { get; } = new();
    public Mock<IMaintenanceIntakeDamageService> IntakeDamages { get; } = new();
    public Mock<IMaintenanceDocumentService> MaintenanceDocumentService { get; } = new();
    public Mock<IVehicleQueryService> VehicleService { get; } = new();
    public Mock<IMediaAccessService> Media { get; } = new();
    public Mock<IMaintenancePhotoService> MaintenancePhotos { get; } = new();
    public Mock<IFileStorageService> FileStorage { get; } = new();
    public Mock<IFeatureAccessService> Features { get; } = new();
    public Mock<IOrganizationSubscriptionService> OrganizationAccess { get; } = new();
    public Mock<IOrganizationQueryService> Organization { get; } = new();
    public Mock<ICatalogQueryService> ScenarioCatalog { get; } = new();
    public PdfDocumentExportService Pdf { get; }
    public WordDocumentExportService Word { get; }
    public GetVehicleDto Vehicle { get; } = CreateVehicle();
    public GetMaintenanceByIdDto Maintenance { get; } = CreateMaintenance();
    public OrganizationDto OrganizationDetails { get; } = CreateOrganization();
    public OrganizationScenarioCatalogItemDto Template { get; private set; } = CreateTemplate();
    public IReadOnlyList<GetMaintenanceDto> VehicleHistory { get; private set; } = [CreateHistory(Guid.NewGuid(), "Замена масла", 1500, new DateTime(2025, 2, 3))];
    public Guid? VehiclePhotoId
    {
        get => this.Vehicle.PhotoId;
        set => this.Vehicle.PhotoId = value;
    }

    public void AddVehicleHistory(params GetMaintenanceDto[] histories)
    {
        this.VehicleHistory = histories;
        this.MaintenanceQueryService.Setup(service => service.Get(
                DocumentExportTests.UserId, DocumentExportTests.OrganizationId, DocumentExportTests.VehicleId))
            .ReturnsAsync(this.VehicleHistory);
    }

    public void SetTemplate(string documentKindCode, string name, bool isActive = true)
    {
        this.Template = new OrganizationScenarioCatalogItemDto
        {
            Id = DocumentExportTests.TemplateId,
            Name = name,
            DocumentKindCode = documentKindCode,
            IsActive = isActive
        };
        this.ScenarioCatalog.Setup(service => service.GetAsync(
                DocumentExportTests.UserId, DocumentExportTests.OrganizationId))
            .ReturnsAsync(new OrganizationScenarioCatalogDto { Documents = [this.Template] });
    }

    public void SetScenarioDocuments(params OrganizationScenarioCatalogItemDto[] documents)
    {
        this.ScenarioCatalog.Setup(service => service.GetAsync(
                DocumentExportTests.UserId, DocumentExportTests.OrganizationId))
            .ReturnsAsync(new OrganizationScenarioCatalogDto { Documents = documents });
    }

    public void SetMaintenancePhotos(params MaintenancePhotoDto[] photos)
    {
        this.MaintenancePhotos.Setup(service => service.GetMaintenancePhotosAsync(
                DocumentExportTests.UserId, DocumentExportTests.OrganizationId, DocumentExportTests.MaintenanceId))
            .ReturnsAsync(photos);
    }

    public void AllowPhotoAccess()
    {
        this.Media.Setup(service => service.UserHasAccessToMediaAsync(DocumentExportTests.UserId, It.IsAny<Guid>()))
            .ReturnsAsync(true);
        this.Media.Setup(service => service.GetMediaPathAsync(DocumentExportTests.UserId, It.IsAny<Guid>()))
            .ReturnsAsync("documents/photo.jpg");
        this.FileStorage.Setup(service => service.FileExistsAsync("documents/photo.jpg"))
            .ReturnsAsync(true);
    }

    public void ConfigurePhoto(byte[] imageData)
    {
        this.FileStorage.Setup(service => service.GetFileAsync("documents/photo.jpg"))
            .ReturnsAsync(() => new MemoryStream(imageData));
    }

    public void ConfigurePhoto(Guid photoId, byte[] imageData)
    {
        var path = $"documents/{photoId:N}.jpg";
        this.Media.Setup(service => service.GetMediaPathAsync(DocumentExportTests.UserId, photoId))
            .ReturnsAsync(path);
        this.FileStorage.Setup(service => service.FileExistsAsync(path))
            .ReturnsAsync(true);
        this.FileStorage.Setup(service => service.GetFileAsync(path))
            .ReturnsAsync(() => new MemoryStream(imageData));
    }

    public TrackingPhotoStream ConfigurePhotoReadFailure(Guid photoId)
    {
        var path = $"documents/{photoId:N}.jpg";
        var stream = new TrackingPhotoStream(throwOnRead: true);
        this.Media.Setup(service => service.GetMediaPathAsync(DocumentExportTests.UserId, photoId))
            .ReturnsAsync(path);
        this.FileStorage.Setup(service => service.FileExistsAsync(path))
            .ReturnsAsync(true);
        this.FileStorage.Setup(service => service.GetFileAsync(path))
            .ReturnsAsync(stream);

        return stream;
    }

    public void ConfigureMissingPhoto(Guid photoId)
    {
        var path = $"documents/{photoId:N}.jpg";
        this.Media.Setup(service => service.GetMediaPathAsync(DocumentExportTests.UserId, photoId))
            .ReturnsAsync(path);
        this.FileStorage.Setup(service => service.FileExistsAsync(path))
            .ReturnsAsync(false);
    }

    public void ConfigurePhotoPathFailure(Guid photoId)
    {
        this.Media.Setup(service => service.GetMediaPathAsync(DocumentExportTests.UserId, photoId))
            .ThrowsAsync(new IOException("Не удалось получить путь фотографии."));
    }

    public void ConfigureEmptyPhotoPath(Guid photoId)
    {
        this.Media.Setup(service => service.GetMediaPathAsync(DocumentExportTests.UserId, photoId))
            .ReturnsAsync(string.Empty);
    }

    public void ConfigurePhotoOpenFailure(Guid photoId)
    {
        var path = $"documents/{photoId:N}.jpg";
        this.Media.Setup(service => service.GetMediaPathAsync(DocumentExportTests.UserId, photoId))
            .ReturnsAsync(path);
        this.FileStorage.Setup(service => service.FileExistsAsync(path))
            .ReturnsAsync(true);
        this.FileStorage.Setup(service => service.GetFileAsync(path))
            .ThrowsAsync(new IOException("Не удалось открыть фотографию."));
    }

    public void VerifyNoGenerationOrHistory()
    {
        this.PdfGenerator.Verify(generator => generator.GenerateVehiclePdfAsync(It.IsAny<VehiclePdfModel>(), It.IsAny<bool>()), Times.Never);
        this.PdfGenerator.Verify(generator => generator.GenerateMaintenancePdfAsync(It.IsAny<MaintenancePdfModel>(), It.IsAny<bool>()), Times.Never);
        this.PdfGenerator.Verify(generator => generator.GenerateMaintenanceIntakePdfAsync(It.IsAny<MaintenanceIntakePdfModel>()), Times.Never);
        this.PdfGenerator.Verify(generator => generator.GenerateMaintenanceWorkActPdfAsync(It.IsAny<MaintenanceWorkActPdfModel>()), Times.Never);
        this.PdfGenerator.Verify(generator => generator.GenerateMaintenanceWorkOrderPdfAsync(It.IsAny<MaintenanceWorkActPdfModel>()), Times.Never);
        this.PdfGenerator.Verify(generator => generator.GenerateScenarioMaintenanceDocumentPdfAsync(It.IsAny<ScenarioMaintenanceDocumentPdfModel>()), Times.Never);
        this.DocxGenerator.Verify(generator => generator.GenerateVehicleHistoryDocxAsync(It.IsAny<WordVehicleModel>(), It.IsAny<bool>()), Times.Never);
        this.DocxGenerator.Verify(generator => generator.GenerateMaintenanceDocxAsync(It.IsAny<WordMaintenanceModel>(), It.IsAny<bool>()), Times.Never);
        this.DocxGenerator.Verify(generator => generator.GenerateMaintenanceWorkActDocxAsync(It.IsAny<WordMaintenanceWorkActModel>()), Times.Never);
        this.DocxGenerator.Verify(generator => generator.GenerateMaintenanceWorkOrderDocxAsync(It.IsAny<WordMaintenanceWorkActModel>()), Times.Never);
        this.DocxGenerator.Verify(generator => generator.GenerateScenarioMaintenanceDocumentDocxAsync(It.IsAny<WordScenarioMaintenanceDocumentModel>()), Times.Never);
        this.VehicleService.As<IVehicleDocumentHistoryService>().Verify(service => service.TrackDocumentGenerated(
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
        this.MaintenanceDocumentService.Verify(service => service.TrackDocumentGenerated(
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    public static GetMaintenanceDto CreateHistory(Guid id, string name, decimal price, DateTime date)
    {
        return new GetMaintenanceDto
        {
            Id = id,
            Status = MaintenanceStatus.Completed,
            Type = MaintenanceType.Repair,
            Date = date,
            PlannedDate = null,
            CompletedDate = date,
            Location = "Сервис",
            Performer = "Мастер",
            Mileage = 12345,
            Comment = "Работа выполнена",
            Price = price,
            PhotoIds = [],
            MaintenanceDetails = [new MaintenanceDetailDto { Name = name, Description = "Историческая строка", Price = price, Quantity = 1 }],
            LinkedDefects = []
        };
    }

    internal sealed class TrackingPhotoStream(bool throwOnRead) : MemoryStream
    {
        public bool IsDisposed { get; private set; }

        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            if (throwOnRead)
            {
                return Task.FromException(new IOException("Не удалось прочитать фотографию."));
            }

            return base.CopyToAsync(destination, bufferSize, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            this.IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    private static GetVehicleDto CreateVehicle()
    {
        return new GetVehicleDto
        {
            Id = DocumentExportTests.VehicleId,
            Kind = VehicleKind.PassengerCar,
            MileageUnit = VehicleMileageUnit.EngineHours,
            BrandId = null,
            BrandName = "Мастерская",
            ModelId = null,
            ModelName = "Грузовик",
            YearIssue = 2020,
            PhotoId = null,
            IsArchived = false,
            MaintenanceCount = 2,
            Name = "Бортовой",
            Vin = "VIN123",
            RegistrationPlate = "А123АА154",
            Description = "Автомобиль клиента",
            IsManualEntry = true,
            ManualBrandName = "Мастерская",
            ManualModelName = "Грузовик",
            OrganizationId = DocumentExportTests.OrganizationId,
            OrganizationClientName = "Клиент"
        };
    }

    private static GetMaintenanceByIdDto CreateMaintenance()
    {
        return new GetMaintenanceByIdDto
        {
            Id = DocumentExportTests.MaintenanceId,
            VehicleId = DocumentExportTests.VehicleId,
            Status = MaintenanceStatus.InProgress,
            Type = MaintenanceType.Repair,
            Date = new DateTime(2025, 2, 3),
            PlannedDate = new DateTime(2025, 2, 4),
            PlannedStartTime = null,
            PlannedEndTime = null,
            PlannedEndDate = null,
            CompletedDate = null,
            Location = "Пост 1",
            Performer = "Мастер СТО",
            Mileage = 654321,
            Comment = "Согласовано с клиентом",
            Price = 4250.75m,
            IntakeNotes = "Приёмка: царапина",
            IntakeMileage = 654321,
            IntakeCompleteness = "Осмотрена",
            IntakeVisibleDamage = "Скол на стекле",
            IntakeRecordedAt = new DateTime(2025, 2, 3, 8, 30, 0, DateTimeKind.Utc),
            GrossAmount = 12000,
            DiscountAmount = 1000,
            PaidAmount = 5000,
            DebtAmount = 6000,
            MaterialCostAmount = 700,
            LaborCostAmount = 800,
            ProfitAmount = 9500,
            PaymentStatus = "partial",
            PhotoIds = [],
            MaintenanceDetails = [new MaintenanceDetailDto
            {
                Name = "Заменить узел",
                Description = "Историческая строка услуги",
                Price = 4250.75m,
                Quantity = 3
            }],
            LinkedDefects = []
        };
    }

    private static OrganizationDto CreateOrganization()
    {
        return new OrganizationDto
        {
            Id = DocumentExportTests.OrganizationId,
            Name = "СТО Кириллица",
            LegalName = "",
            Inn = "ИНН",
            Kpp = "КПП",
            Ogrn = "ОГРН",
            LegalAddress = "Юридический адрес",
            ActualAddress = "Фактический адрес",
            Phone = "+7 900 000-00-00",
            Email = "sto@example.test",
            SignatoryPosition = null,
            SignatoryName = null,
            SignatoryBasis = null,
            DocumentStyleCode = "classic",
            PrimaryScenarioCode = "autoservice"
        };
    }

    private static OrganizationScenarioCatalogItemDto CreateTemplate()
    {
        return new OrganizationScenarioCatalogItemDto
        {
            Id = DocumentExportTests.TemplateId,
            Name = "Шаблон документа",
            DocumentKindCode = OrganizationScenarioDocumentKindCodes.WorkOrder,
            IsActive = true
        };
    }
}
