using System.Collections;
using OpenCRM.Domain.Enums;
using OpenCRM.Modules.Documents;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Vehicles.Contracts.Dtos;
using Moq;

namespace OpenCRM.Modules.Documents.Tests.Unit;

public sealed class MaintenanceReportDataLoaderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoadForwardsScopeAndLoadsVehicleAfterMaintenanceWithoutEnumeratingDetails(bool organizationScope)
    {
        var userId = "document-owner";
        Guid? organizationId = organizationScope ? Guid.NewGuid() : null;
        var maintenanceId = Guid.NewGuid();
        var maintenance = MaintenanceReportDataLoaderTests.CreateMaintenance(maintenanceId, Guid.NewGuid());
        var vehicle = new DocumentExportTestFixture().Vehicle;
        var callOrder = new List<string>();
        var maintenances = new Mock<IMaintenanceQueryService>(MockBehavior.Strict);
        var vehicles = new Mock<IVehicleQueryService>(MockBehavior.Strict);
        maintenances.Setup(service => service.GetById(userId, organizationId, maintenanceId))
            .Callback(() => callOrder.Add("maintenance"))
            .ReturnsAsync(maintenance);
        vehicles.Setup(service => service.GetById(userId, organizationId, maintenance.VehicleId))
            .Callback(() => callOrder.Add("vehicle"))
            .ReturnsAsync(vehicle);
        var loader = new MaintenanceReportDataLoader(maintenances.Object, vehicles.Object);

        var result = await loader.LoadAsync(userId, organizationId, maintenanceId);

        Assert.NotNull(result);
        Assert.Same(maintenance, result.Maintenance);
        Assert.Same(vehicle, result.Vehicle);
        Assert.Equal(new[] { "maintenance", "vehicle" }, callOrder);
        maintenances.VerifyAll();
        vehicles.VerifyAll();
    }

    [Fact]
    public async Task MissingMaintenanceDoesNotLoadVehicle()
    {
        const string userId = "missing";
        var organizationId = Guid.NewGuid();
        var maintenanceId = Guid.NewGuid();
        var maintenances = new Mock<IMaintenanceQueryService>(MockBehavior.Strict);
        var vehicles = new Mock<IVehicleQueryService>(MockBehavior.Strict);
        maintenances.Setup(service => service.GetById(userId, organizationId, maintenanceId))
            .ReturnsAsync((GetMaintenanceByIdDto)null!);
        var loader = new MaintenanceReportDataLoader(maintenances.Object, vehicles.Object);

        var result = await loader.LoadAsync(userId, organizationId, maintenanceId);

        Assert.Null(result);
        maintenances.VerifyAll();
        vehicles.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MissingVehicleKeepsLoadedMaintenance()
    {
        const string userId = "document-owner";
        Guid? organizationId = null;
        var maintenanceId = Guid.NewGuid();
        var maintenance = MaintenanceReportDataLoaderTests.CreateMaintenance(maintenanceId, Guid.NewGuid());
        var maintenances = new Mock<IMaintenanceQueryService>(MockBehavior.Strict);
        var vehicles = new Mock<IVehicleQueryService>(MockBehavior.Strict);
        maintenances.Setup(service => service.GetById(userId, organizationId, maintenanceId))
            .ReturnsAsync(maintenance);
        vehicles.Setup(service => service.GetById(userId, organizationId, maintenance.VehicleId))
            .ReturnsAsync((GetVehicleDto)null!);
        var loader = new MaintenanceReportDataLoader(maintenances.Object, vehicles.Object);

        var result = await loader.LoadAsync(userId, organizationId, maintenanceId);

        Assert.NotNull(result);
        Assert.Same(maintenance, result.Maintenance);
        Assert.Null(result.Vehicle);
        maintenances.VerifyAll();
        vehicles.VerifyAll();
    }

    [Fact]
    public async Task MaintenanceReadExceptionDoesNotLoadVehicle()
    {
        var expectedException = new InvalidOperationException("Ошибка чтения обслуживания.");
        var maintenances = new Mock<IMaintenanceQueryService>(MockBehavior.Strict);
        var vehicles = new Mock<IVehicleQueryService>(MockBehavior.Strict);
        maintenances.Setup(service => service.GetById("document-owner", null, It.IsAny<Guid>()))
            .ThrowsAsync(expectedException);
        var loader = new MaintenanceReportDataLoader(maintenances.Object, vehicles.Object);

        var actualException = await Assert.ThrowsAsync<InvalidOperationException>(
            () => loader.LoadAsync("document-owner", null, Guid.NewGuid()));

        Assert.Same(expectedException, actualException);
        vehicles.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task VehicleReadExceptionPassesThrough()
    {
        var expectedException = new InvalidOperationException("Ошибка чтения техники.");
        var maintenanceId = Guid.NewGuid();
        var maintenance = MaintenanceReportDataLoaderTests.CreateMaintenance(maintenanceId, Guid.NewGuid());
        var maintenances = new Mock<IMaintenanceQueryService>(MockBehavior.Strict);
        var vehicles = new Mock<IVehicleQueryService>(MockBehavior.Strict);
        maintenances.Setup(service => service.GetById("document-owner", null, maintenanceId))
            .ReturnsAsync(maintenance);
        vehicles.Setup(service => service.GetById("document-owner", null, maintenance.VehicleId))
            .ThrowsAsync(expectedException);
        var loader = new MaintenanceReportDataLoader(maintenances.Object, vehicles.Object);

        var actualException = await Assert.ThrowsAsync<InvalidOperationException>(
            () => loader.LoadAsync("document-owner", null, maintenanceId));

        Assert.Same(expectedException, actualException);
        maintenances.VerifyAll();
        vehicles.VerifyAll();
    }

    private static GetMaintenanceByIdDto CreateMaintenance(Guid maintenanceId, Guid vehicleId) => new()
    {
        Id = maintenanceId,
        Status = MaintenanceStatus.Completed,
        Type = MaintenanceType.Maintenance,
        Date = new DateTime(2026, 10, 9),
        PlannedDate = null,
        CompletedDate = null,
        Location = null,
        Performer = null,
        Mileage = null,
        Comment = null,
        Price = null,
        VehicleId = vehicleId,
        PhotoIds = [],
        MaintenanceDetails = new ThrowingEnumerable<MaintenanceDetailDto>(),
        LinkedDefects = []
    };

    private sealed class ThrowingEnumerable<T> : IEnumerable<T>
    {
        public IEnumerator<T> GetEnumerator() => throw new InvalidOperationException("Строки отчёта перечислены при загрузке.");

        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
    }
}
