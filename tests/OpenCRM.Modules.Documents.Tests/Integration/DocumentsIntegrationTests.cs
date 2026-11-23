using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Services.Interfaces.Documents.Dtos;
using OpenCRM.Modules.Maintenance.Contracts.Photos;
using OpenCRM.Testing;
using OpenCRM.Modules.Catalog.Contracts;
using OpenCRM.Modules.Catalog;
using OpenCRM.Modules.Organizations;
using OpenCRM.Modules.Organizations.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Domain.Interfaces.Maintenances;
using OpenCRM.Domain.Interfaces.Media;
using OpenCRM.Domain.Interfaces.Vehicles;
using OpenCRM.Modules.Billing.Features;
using OpenCRM.Modules.Maintenance;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Billing;
using OpenCRM.Modules.Documents.Pdf;
using OpenCRM.Modules.Vehicles;
using OpenCRM.Modules.Vehicles.Counters;
using OpenCRM.Modules.Documents.Word;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.Repositories;
using OpenCRM.Modules.Documents;
using OpenCRM.Services.Interfaces.Documents;
using OpenCRM.Services.Interfaces.Features;
using OpenCRM.Services.Interfaces.FileStorages;
using OpenCRM.Services.Interfaces.Media;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Payments;
using OpenCRM.Services.Interfaces.Promotions;
using OpenCRM.Services.Interfaces.Subscriptions;
using Moq;

namespace OpenCRM.Modules.Documents.Tests.Integration;

public sealed class DocumentsIntegrationTests(DocumentsDatabaseFixture fixture) : IClassFixture<DocumentsDatabaseFixture>
{
    [Theory]
    [InlineData("vehicle-pdf")]
    [InlineData("vehicle-word")]
    [InlineData("maintenance-pdf")]
    [InlineData("maintenance-word")]
    [InlineData("intake-pdf")]
    [InlineData("act-pdf")]
    [InlineData("act-word")]
    [InlineData("order-pdf")]
    [InlineData("order-word")]
    [InlineData("scenario-pdf")]
    [InlineData("scenario-word")]
    public async Task ExportReadsRelationalDataAndPersistsHistory(string operation)
    {
        await using var context = fixture.CreateContext();
        var work = await DocumentsIntegrationTests.SeedAsync(context);
        using var services = DocumentsIntegrationTests.CreateServices(context);
        var organizationId = work.Vehicle.OrganizationId!.Value;
        var templateId = await context.OrganizationScenarioCatalogItems
            .Where(item => item.OrganizationId == organizationId && item.Code == "integration-document")
            .Select(item => item.Id).SingleAsync();

        var result = await DocumentsIntegrationTests.ExportAsync(services, operation, work.UserId, organizationId,
            work.VehicleId, work.Id, templateId);

        Assert.Equal(DocumentExportOutcome.File, result.Outcome);
        Assert.NotEmpty(result.Content!);
        if (operation.EndsWith("pdf", StringComparison.Ordinal))
        {
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(result.Content!, 0, 4));
        }
        else
        {
            Assert.Equal((byte)'P', result.Content![0]);
            Assert.Equal((byte)'K', result.Content[1]);
        }

        context.ChangeTracker.Clear();
        var historyCount = await context.MaintenanceHistoryEvents
            .CountAsync(item => item.MaintenanceId == work.Id && item.EventType == "document_generated");
        var vehicleHistoryCount = await context.VehicleHistoryEvents
            .CountAsync(item => item.VehicleId == work.VehicleId && item.EventType == "document_generated");
        Assert.Equal(operation == "intake-pdf" || operation.StartsWith("vehicle", StringComparison.Ordinal) ? 0 : 1, historyCount);
        Assert.Equal(operation.StartsWith("vehicle", StringComparison.Ordinal) ? 1 : 0, vehicleHistoryCount);
        var saved = await context.Maintenance.SingleAsync(item => item.Id == work.Id);
        Assert.Equal("Историческая работа", Assert.Single(saved.Details).Name);
        Assert.Equal(1250.5m, Assert.Single(saved.Details).Price);
    }

    [Theory]
    [InlineData("maintenance-pdf")]
    [InlineData("maintenance-word")]
    [InlineData("intake-pdf")]
    public async Task MissingMaintenanceInDatabaseFailsBeforeGenerationAndHistory(string operation)
    {
        await using var context = fixture.CreateContext();
        var work = await DocumentsIntegrationTests.SeedAsync(context);
        using var services = DocumentsIntegrationTests.CreateServices(context);
        var maintenanceId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<Exception>(() => DocumentsIntegrationTests.ExportAsync(
            services, operation, work.UserId, work.Vehicle.OrganizationId!.Value,
            work.VehicleId, maintenanceId, Guid.NewGuid()));

        Assert.Contains("не найдено обслуживание", exception.Message);
        Assert.Contains(maintenanceId.ToString(), exception.Message);
        Assert.False(await context.MaintenanceHistoryEvents.AnyAsync(item => item.MaintenanceId == maintenanceId));
        Assert.False(await context.VehicleHistoryEvents.AnyAsync(item =>
            item.VehicleId == work.VehicleId && item.EventType == "document_generated"));
    }

    [Fact]
    public async Task ForeignUserCannotExportOrCreateHistory()
    {
        await using var context = fixture.CreateContext();
        var work = await DocumentsIntegrationTests.SeedAsync(context);
        var stranger = new ApplicationUser();
        context.Users.Add(stranger);
        await context.SaveChangesAsync();
        using var services = DocumentsIntegrationTests.CreateServices(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => services.Pdf.GenerateMaintenancePdf(
            stranger.Id, work.Id, false, work.Vehicle.OrganizationId));

        Assert.False(await context.MaintenanceHistoryEvents.AnyAsync(item => item.MaintenanceId == work.Id));
    }

    [Fact]
    public async Task WorkActNumberIsStableAcrossConcurrentExports()
    {
        await using var context = fixture.CreateContext();
        var work = await DocumentsIntegrationTests.SeedAsync(context);
        var userId = work.UserId;
        var organizationId = work.Vehicle.OrganizationId!.Value;
        var maintenanceId = work.Id;

        var numbers = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var exportContext = fixture.CreateContext();
            using var services = DocumentsIntegrationTests.CreateServices(exportContext);

            return await services.MaintenanceDocuments.EnsureWorkActNumber(userId, organizationId, maintenanceId);
        }));

        Assert.All(numbers, number => Assert.Equal(numbers[0], number));
        Assert.Equal(1, numbers[0]);
        context.ChangeTracker.Clear();
        var saved = await context.Maintenance.SingleAsync(item => item.Id == maintenanceId);
        Assert.Equal(numbers[0], saved.WorkActNumber);
        Assert.Equal(organizationId, saved.WorkActOrganizationId);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public async Task ExpiredSubscriptionKeepsFreeExportAndViewerUsesExportPermission(bool expired, bool word, bool viewerExportAllowed)
    {
        await using var context = fixture.CreateContext();
        var work = await DocumentsIntegrationTests.SeedAsync(context);
        var organizationId = work.Vehicle.OrganizationId!.Value;
        var userId = work.UserId;
        if (expired)
        {
            var subscription = await context.OrganizationSubscriptions.SingleAsync(item => item.OrganizationId == organizationId);
            subscription.EndDate = DateTime.UtcNow.AddHours(-1);
        }
        else
        {
            var viewer = new ApplicationUser();
            context.OrganizationMembers.Add(new OrganizationMember
            {
                OrganizationId = organizationId, User = viewer, RoleCode = OrganizationRoleCodes.Viewer
            });
            userId = viewer.Id;
            if (!viewerExportAllowed)
            {
                context.OrganizationRolePermissionOverrides.Add(new OrganizationRolePermissionOverride
                {
                    OrganizationId = organizationId,
                    RoleCode = OrganizationRoleCodes.Viewer,
                    PermissionCode = OrganizationPermissionCodes.DocumentsExport,
                    IsEnabled = false,
                    UpdatedByUserId = work.UserId
                });
            }
        }

        await context.SaveChangesAsync();
        using var services = DocumentsIntegrationTests.CreateServices(context);

        var result = word
            ? await services.Word.GenerateMaintenanceWord(userId, work.Id, false, organizationId)
            : await services.Pdf.GenerateMaintenancePdf(userId, work.Id, false, organizationId);

        if (expired || viewerExportAllowed)
        {
            Assert.Equal(DocumentExportOutcome.File, result.Outcome);
            Assert.NotEmpty(result.Content!);
            Assert.True(await context.MaintenanceHistoryEvents.AnyAsync(item => item.MaintenanceId == work.Id));
            return;
        }

        Assert.Equal(DocumentExportOutcome.AccessDenied, result.Outcome);
        Assert.Null(result.Content);
        Assert.False(await context.MaintenanceHistoryEvents.AnyAsync(item => item.MaintenanceId == work.Id));
    }

    private static Task<DocumentExportResult> ExportAsync(
        ExportServices services, string operation, string userId, Guid organizationId,
        Guid vehicleId, Guid maintenanceId, Guid templateId) => operation switch
    {
        "vehicle-pdf" => services.Pdf.GenerateVehiclePdf(userId, vehicleId, false, organizationId),
        "vehicle-word" => services.Word.GenerateVehicleWord(userId, vehicleId, false, organizationId),
        "maintenance-pdf" => services.Pdf.GenerateMaintenancePdf(userId, maintenanceId, false, organizationId),
        "maintenance-word" => services.Word.GenerateMaintenanceWord(userId, maintenanceId, false, organizationId),
        "intake-pdf" => services.Pdf.PrintMaintenanceIntake(userId, maintenanceId, organizationId),
        "act-pdf" => services.Pdf.GenerateMaintenanceWorkActPdf(userId, maintenanceId, organizationId),
        "act-word" => services.Word.GenerateMaintenanceWorkActWord(userId, maintenanceId, organizationId),
        "order-pdf" => services.Pdf.GenerateMaintenanceWorkOrderPdf(userId, maintenanceId, organizationId),
        "order-word" => services.Word.GenerateMaintenanceWorkOrderWord(userId, maintenanceId, organizationId),
        "scenario-pdf" => services.Pdf.GenerateScenarioMaintenanceDocumentPdf(userId, maintenanceId, templateId, organizationId),
        "scenario-word" => services.Word.GenerateScenarioMaintenanceDocumentWord(userId, maintenanceId, templateId, organizationId),
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

    private static async Task<OpenCRM.Domain.Maintenance> SeedAsync(OpenCRMContext context)
    {
        await FeatureBootstrapper.EnsureDefaultsAsync(context);
        // Проверка опирается на тарифы организации: коммерческий режим включается явно.
        await FeatureBootstrapper.EnsureCommercialPlansAsync(context);
        var user = new ApplicationUser();
        var organization = new Organization
        {
            Name = "Документы СТО", OwnerUser = user,
            PrimaryScenarioCode = OrganizationTypeCodes.ServiceStation,
            Members = [new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Owner }]
        };
        var work = new OpenCRM.Domain.Maintenance
        {
            User = user,
            Vehicle = new Vehicle { User = user, Organization = organization, ManualBrandName = "Toyota", ManualModelName = "Camry" },
            Date = DateTime.UtcNow, DateTimeCreation = DateTime.UtcNow, CompletedDate = DateTime.UtcNow,
            Type = MaintenanceType.Maintenance, Status = MaintenanceStatus.Completed,
            IntakeNotes = "Ключи приняты", IntakeRecordedAt = DateTime.UtcNow,
            Details = [new() { Name = "Историческая работа", Price = 1250.5m }]
        };
        context.Maintenance.Add(work);
        var planId = await context.SubscriptionPlans.Where(plan => plan.IsDemoPlan
                && plan.PlanTypeCode == SubscriptionPlanCatalogCodes.PlanTypes.Business)
            .Select(plan => plan.Id).FirstAsync();
        context.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            Organization = organization, SubscriptionPlanId = planId,
            StartDate = DateTime.UtcNow.AddDays(-1), EndDate = DateTime.UtcNow.AddDays(7)
        });
        context.OrganizationScenarioCatalogItems.Add(new OrganizationScenarioCatalogItem
        {
            Organization = organization, KindCode = OrganizationScenarioCatalogKindCodes.Document,
            Code = "integration-document", Name = "Осмотр техники", DocumentKindCode = OrganizationScenarioDocumentKindCodes.InspectionAct,
            ScenarioCode = OrganizationTypeCodes.ServiceStation
        });
        await context.SaveChangesAsync();

        return work;
    }

    private static ExportServices CreateServices(OpenCRMContext context)
    {
        var audit = Mock.Of<IOrganizationAuditService>();
        var permissions = new OrganizationPermissionService(context, audit);
        var subscriptions = new OrganizationSubscriptionService(
            Mock.Of<IOrganizationSubscriptionCatalogQueryService>(),
            OrganizationBillingTestServices.CreateEntitlement(context),
            OrganizationBillingTestServices.CreateAccess(context, permissions),
            Mock.Of<IOrganizationSubscriptionCheckoutService>(),
            OrganizationBillingTestServices.CreateExtensionQuery(context, permissions),
            Mock.Of<IOrganizationSubscriptionCommandService>(),
            Mock.Of<IOrganizationSubscriptionActivationService>(),
            Mock.Of<IOrganizationSubscriptionPurchasePolicyService>());
        var vehicleRepository = new VehicleRepository(context);
        var clock = new Mock<ISystemClock>();
        clock.SetupGet(value => value.UtcNow).Returns(DateTime.UtcNow);
        var vehicles = new VehicleQueryService(vehicleRepository,
            new VehiclePhotoRepository(context, NullLogger<VehiclePhotoRepository>.Instance), context, subscriptions, clock.Object);
        var vehicleHistory = new VehicleHistoryWriter(context, clock.Object, vehicleRepository, subscriptions);
        var maintenanceRepository = new MaintenanceRepository(context);
        var maintenanceServices = new ServiceCollection();
        maintenanceServices.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        maintenanceServices.AddLogging();
        maintenanceServices.AddScoped<OpenCRM.Modules.Vehicles.Contracts.IVehicleMaintenanceLookupService, OpenCRM.Modules.Vehicles.VehicleMaintenanceLookupService>();
        maintenanceServices.AddOpenCRMMaintenance();
        maintenanceServices.AddScoped(_ => Mock.Of<OpenCRM.Modules.Clients.Contracts.IOrganizationInquiryOriginReader>());
        maintenanceServices.AddOpenCRMCatalog();
        maintenanceServices.AddOpenCRMOrganizations();
        maintenanceServices.AddScoped<IOrganizationPermissionService>(_ => permissions);
        maintenanceServices.AddScoped(_ => context);
        maintenanceServices.AddScoped<IMaintenanceRepository>(_ => maintenanceRepository);
        maintenanceServices.AddScoped<IMaintenancePhotoRepository>(_ => new MaintenancePhotoRepository(
            context, NullLogger<MaintenancePhotoRepository>.Instance));
        maintenanceServices.AddScoped<IVehicleRepository>(_ => vehicleRepository);
        maintenanceServices.AddScoped<ISystemClock>(_ => clock.Object);
        maintenanceServices.AddScoped<IOrganizationSubscriptionService>(_ => subscriptions);
        var provider = maintenanceServices.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var scope = provider.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<IMaintenanceQueryService>();
        var maintenanceDocuments = scope.ServiceProvider.GetRequiredService<IMaintenanceDocumentService>();
        var organizations = scope.ServiceProvider.GetRequiredService<IOrganizationQueryService>();
        var catalog = scope.ServiceProvider.GetRequiredService<ICatalogQueryService>();
        var loader = new VehicleDocumentDataLoader(vehicles, query);
        var maintenanceReportLoader = new MaintenanceReportDataLoader(query, vehicles);
        var media = new Mock<IMediaAccessService>();
        var maintenancePhotos = new Mock<IMaintenancePhotoService>();
        maintenancePhotos.Setup(service => service.GetMaintenancePhotosAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>()))
            .ReturnsAsync(Array.Empty<OpenCRM.Modules.Maintenance.Contracts.Photos.MaintenancePhotoDto>());
        var access = new DocumentExportAccess(Mock.Of<IFeatureAccessService>(), subscriptions,
            Mock.Of<IPlatformCommercialPolicy>(policy => policy.CommercialEnabled == true));
        var photoLoader = new DocumentPhotoLoader(maintenancePhotos.Object, media.Object, Mock.Of<IFileStorageService>());
        var pdfGenerator = new QuestPdfGeneratorService();
        var docxGenerator = new DocxGeneratorService();
        var vehicleReportWorkflow = new VehicleReportWorkflow(pdfGenerator, docxGenerator, vehicleHistory, loader, access, photoLoader);
        var maintenanceReportWorkflow = new MaintenanceReportWorkflow(
            pdfGenerator, docxGenerator, maintenanceDocuments, maintenanceReportLoader, access, photoLoader);
        var workDocumentsWorkflow = new WorkDocumentsWorkflow(
            pdfGenerator, docxGenerator, query, maintenanceDocuments, vehicles, organizations, access);
        var scenarioDocumentWorkflow = new ScenarioDocumentWorkflow(
            pdfGenerator, docxGenerator, query, maintenanceDocuments, vehicles, organizations, catalog, access, photoLoader);
        var maintenanceIntakeWorkflow = new MaintenanceIntakeWorkflow(
            pdfGenerator, query, vehicles, organizations, access, photoLoader,
            scope.ServiceProvider.GetRequiredService<IMaintenanceIntakeDamageService>());
        var pdf = new PdfDocumentExportService(
            vehicleReportWorkflow, maintenanceReportWorkflow, workDocumentsWorkflow, scenarioDocumentWorkflow, maintenanceIntakeWorkflow);
        var word = new WordDocumentExportService(
            vehicleReportWorkflow, maintenanceReportWorkflow, workDocumentsWorkflow, scenarioDocumentWorkflow);

        return new ExportServices(pdf, word, maintenanceDocuments, provider, scope);
    }

    private sealed class ExportServices(
        PdfDocumentExportService pdf,
        WordDocumentExportService word,
        IMaintenanceDocumentService maintenanceDocuments,
        ServiceProvider provider,
        IServiceScope scope) : IDisposable
    {
        public PdfDocumentExportService Pdf { get; } = pdf;
        public WordDocumentExportService Word { get; } = word;
        public IMaintenanceDocumentService MaintenanceDocuments { get; } = maintenanceDocuments;

        public void Dispose()
        {
            scope.Dispose();
            provider.Dispose();
        }
    }
}
