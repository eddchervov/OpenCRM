using OpenCRM.Modules.Maintenance.Contracts.Photos;
using OpenCRM.Modules.Catalog.Contracts;
using OpenCRM.Modules.Organizations.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Modules.Documents.Pdf;
using OpenCRM.Modules.Documents.Word;
using OpenCRM.Services.Interfaces.Documents;
using OpenCRM.Services.Interfaces.Features;
using OpenCRM.Services.Interfaces.FileStorages;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Services.Interfaces.Media;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Pdf;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Services.Interfaces.Word;
using OpenCRM.WebApp.Extensions;
using Moq;

namespace OpenCRM.Modules.Documents.Tests.Integration;

public sealed class DocumentsRegistrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ModuleRegistersScopedServicesIndependentlyAndThroughWebComposition(bool composeWebApplication)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DataProtection:KeysDirectory"] = Path.GetTempPath(),
            ["LocalTesting:UseInMemoryNotifications"] = "true"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        if (composeWebApplication)
        {
            services.AddOpenCRMInfrastructure(configuration);
        }
        else
        {
            services.AddOpenCRMDocuments();
        }

        var documentServiceTypes = new[]
        {
            typeof(IVehicleDocumentDataLoader),
            typeof(IMaintenanceReportDataLoader),
            typeof(DocumentExportAccess),
            typeof(DocumentPhotoLoader),
            typeof(VehicleReportWorkflow),
            typeof(MaintenanceReportWorkflow),
            typeof(WorkDocumentsWorkflow),
            typeof(ScenarioDocumentWorkflow),
            typeof(MaintenanceIntakeWorkflow),
            typeof(IPdfGeneratorService),
            typeof(IDocxGeneratorService),
            typeof(IPdfDocumentExportService),
            typeof(IWordDocumentExportService)
        };

        foreach (var serviceType in documentServiceTypes)
        {
            Assert.Single(services, descriptor => descriptor.ServiceType == serviceType);
        }

        services.AddScoped(_ => Mock.Of<IVehicleQueryService>());
        services.AddScoped(_ => Mock.Of<IVehicleDocumentHistoryService>());
        services.AddScoped(_ => Mock.Of<IMaintenanceQueryService>());
        services.AddScoped(_ => Mock.Of<IMaintenanceIntakeDamageService>());
        services.AddScoped(_ => Mock.Of<IMaintenanceDocumentService>());
        services.AddScoped(_ => Mock.Of<IMediaAccessService>());
        services.AddScoped(_ => Mock.Of<IMaintenancePhotoService>());
        services.AddScoped(_ => Mock.Of<IFileStorageService>());
        services.AddScoped(_ => Mock.Of<IFeatureAccessService>());
        services.AddScoped(_ => Mock.Of<IOrganizationSubscriptionService>());
        services.AddScoped(_ => Mock.Of<IOrganizationQueryService>());
        services.AddScoped(_ => Mock.Of<ICatalogQueryService>());
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        DocumentsRegistrationTests.AssertScoped<IVehicleDocumentDataLoader, VehicleDocumentDataLoader>(first, second);
        DocumentsRegistrationTests.AssertScoped<IMaintenanceReportDataLoader, MaintenanceReportDataLoader>(first, second);
        DocumentsRegistrationTests.AssertScoped<DocumentExportAccess, DocumentExportAccess>(first, second);
        DocumentsRegistrationTests.AssertScoped<DocumentPhotoLoader, DocumentPhotoLoader>(first, second);
        DocumentsRegistrationTests.AssertScoped<VehicleReportWorkflow, VehicleReportWorkflow>(first, second);
        DocumentsRegistrationTests.AssertScoped<MaintenanceReportWorkflow, MaintenanceReportWorkflow>(first, second);
        DocumentsRegistrationTests.AssertScoped<WorkDocumentsWorkflow, WorkDocumentsWorkflow>(first, second);
        DocumentsRegistrationTests.AssertScoped<ScenarioDocumentWorkflow, ScenarioDocumentWorkflow>(first, second);
        DocumentsRegistrationTests.AssertScoped<MaintenanceIntakeWorkflow, MaintenanceIntakeWorkflow>(first, second);
        DocumentsRegistrationTests.AssertScoped<IPdfGeneratorService, QuestPdfGeneratorService>(first, second);
        DocumentsRegistrationTests.AssertScoped<IDocxGeneratorService, DocxGeneratorService>(first, second);
        DocumentsRegistrationTests.AssertScoped<IPdfDocumentExportService, PdfDocumentExportService>(first, second);
        DocumentsRegistrationTests.AssertScoped<IWordDocumentExportService, WordDocumentExportService>(first, second);
    }

    private static void AssertScoped<TService, TImplementation>(IServiceScope first, IServiceScope second)
        where TService : class
    {
        var instance = first.ServiceProvider.GetRequiredService<TService>();

        Assert.IsType<TImplementation>(instance);
        Assert.Equal("OpenCRM.Modules.Documents", instance.GetType().Assembly.GetName().Name);
        Assert.Same(instance, first.ServiceProvider.GetRequiredService<TService>());
        Assert.NotSame(instance, second.ServiceProvider.GetRequiredService<TService>());
    }
}
