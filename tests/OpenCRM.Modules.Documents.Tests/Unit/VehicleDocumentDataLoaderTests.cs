using OpenCRM.Modules.Documents;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Vehicles.Contracts.Dtos;
using Moq;

namespace OpenCRM.Modules.Documents.Tests.Unit;

public sealed class VehicleDocumentDataLoaderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoadPreservesScopeAndHistoryOrder(bool organizationScope)
    {
        var userId = "document-owner";
        Guid? organizationId = organizationScope ? Guid.NewGuid() : null;
        var fixture = new DocumentExportTestFixture();
        var vehicle = fixture.Vehicle;
        var history = new[]
        {
            DocumentExportTestFixture.CreateHistory(Guid.NewGuid(), "Поздняя работа", 2000, new DateTime(2026, 10, 9)),
            DocumentExportTestFixture.CreateHistory(Guid.NewGuid(), "Ранняя работа", 1000, new DateTime(2025, 10, 9))
        };
        var vehicles = new Mock<IVehicleQueryService>(MockBehavior.Strict);
        var maintenances = new Mock<IMaintenanceQueryService>(MockBehavior.Strict);
        vehicles.Setup(service => service.GetById(userId, organizationId, vehicle.Id)).ReturnsAsync(vehicle);
        maintenances.Setup(service => service.Get(userId, organizationId, vehicle.Id)).ReturnsAsync(history);
        var loader = new VehicleDocumentDataLoader(vehicles.Object, maintenances.Object);

        var result = await loader.LoadAsync(userId, organizationId, vehicle.Id);

        Assert.Same(vehicle, result.Vehicle);
        Assert.Equal(history, result.MaintenanceHistory);
        vehicles.VerifyAll();
        maintenances.VerifyAll();
    }

    [Fact]
    public async Task MissingVehicleDoesNotLoadHistory()
    {
        var vehicles = new Mock<IVehicleQueryService>();
        var maintenances = new Mock<IMaintenanceQueryService>(MockBehavior.Strict);
        var loader = new VehicleDocumentDataLoader(vehicles.Object, maintenances.Object);

        var result = await loader.LoadAsync("missing", null, Guid.NewGuid());

        Assert.Null(result.Vehicle);
        Assert.Empty(result.MaintenanceHistory);
        maintenances.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReadFailureIsNotConvertedToMissingVehicle()
    {
        var vehicles = new Mock<IVehicleQueryService>();
        vehicles.Setup(service => service.GetById(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid>()))
            .ThrowsAsync(new InvalidOperationException("Доступ запрещён."));
        var maintenances = new Mock<IMaintenanceQueryService>(MockBehavior.Strict);
        var loader = new VehicleDocumentDataLoader(vehicles.Object, maintenances.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => loader.LoadAsync("denied", null, Guid.NewGuid()));

        maintenances.VerifyNoOtherCalls();
    }

    [Fact]
    public void ModuleHasNoDependencyOnLegacyBusinessLogicDataOrWeb()
    {
        var assembly = typeof(VehicleDocumentDataLoader).Assembly;

        Assert.Equal("OpenCRM.Modules.Documents", assembly.GetName().Name);
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference =>
            reference.Name is "OpenCRM.Infrastructure.Common" or "OpenCRM.Infrastructure.Data" or "OpenCRM.WebApp");

        var projectDependencies = assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => name.StartsWith("OpenCRM.", StringComparison.Ordinal) || name == "Helpers")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[]
        {
            "Helpers", "OpenCRM.Domain", "OpenCRM.Modules.Billing.Contracts", "OpenCRM.Modules.Catalog.Contracts",
            "OpenCRM.Modules.Maintenance.Contracts", "OpenCRM.Modules.Organizations.Contracts",
            "OpenCRM.Modules.Vehicles.Contracts", "OpenCRM.Services.Interfaces"
        }, projectDependencies);
        Assert.DoesNotContain(typeof(IVehicleQueryService).Assembly.GetReferencedAssemblies(), reference =>
            reference.Name?.StartsWith("OpenCRM.Infrastructure.", StringComparison.Ordinal) == true
            || reference.Name?.StartsWith("OpenCRM.Modules.", StringComparison.Ordinal) == true
            || reference.Name == "OpenCRM.WebApp");
    }
}
