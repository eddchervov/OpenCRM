using Microsoft.EntityFrameworkCore;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Domain.Interfaces.Vehicles;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.Repositories;
using OpenCRM.Modules.Inventory.Contracts;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Testing;

namespace OpenCRM.Modules.Maintenance.Tests.Integration;

public sealed class MaintenancePersonalAtomicityTests(MaintenanceDatabaseFixture fixture) : IClassFixture<MaintenanceDatabaseFixture>
{
    [Fact]
    public async Task CreateRollsBackMaintenanceAndHistoryWhenCounterSyncFails()
    {
        await using var context = fixture.CreateContext();
        var vehicle = MaintenancePersonalAtomicityTests.CreateVehicle();
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();

        var counter = MaintenancePersonalAtomicityTests.CreateFailingCounter(context, vehicle.User.Id, vehicle.Id);
        using var service = MaintenancePersonalAtomicityTests.CreateService(context, counter.Object);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.Create(
            vehicle.User.Id,
            vehicle.Id,
            MaintenancePersonalAtomicityTests.CreateRequest()));

        Assert.Equal("Сбой синхронизации счётчика.", error.Message);
        counter.Verify(value => value.SyncCounterFromMaintenanceAsync(
            vehicle.User.Id,
            null,
            vehicle.Id,
            It.IsAny<Guid>(),
            null,
            It.IsAny<DateTime>()), Times.Once);
        context.ChangeTracker.Clear();
        Assert.False(await context.Maintenance.AnyAsync(value => value.VehicleId == vehicle.Id));
        Assert.False(await context.MaintenanceHistoryEvents.AnyAsync(value => value.VehicleId == vehicle.Id));
        Assert.False(await context.VehicleHistoryEvents.AnyAsync(value => value.VehicleId == vehicle.Id));
    }

    [Fact]
    public async Task UpdateRollsBackMaintenanceAndHistoryWhenCounterSyncFails()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenancePersonalAtomicityTests.CreateWork();
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        var originalVersion = work.Version;
        var originalDetailId = work.Details.Single().DetailId;
        var originalComment = work.Comment;

        var counter = MaintenancePersonalAtomicityTests.CreateFailingCounter(context, work.User.Id, work.VehicleId);
        using var service = MaintenancePersonalAtomicityTests.CreateService(context, counter.Object);
        var request = MaintenancePersonalAtomicityTests.CreateUpdateRequest(work);
        request.Comment = "Изменённый комментарий";

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.Update(
            work.User.Id,
            work.Id,
            request));

        Assert.Equal("Сбой синхронизации счётчика.", error.Message);
        counter.Verify(value => value.SyncCounterFromMaintenanceAsync(
            work.User.Id,
            null,
            work.VehicleId,
            work.Id,
            null,
            It.IsAny<DateTime>()), Times.Once);
        context.ChangeTracker.Clear();
        var savedWork = await context.Maintenance
            .Include(value => value.Details)
            .SingleAsync(value => value.Id == work.Id);
        Assert.Equal(originalComment, savedWork.Comment);
        Assert.Equal(originalVersion, savedWork.Version);
        Assert.Equal(originalDetailId, savedWork.Details.Single().DetailId);
        Assert.False(await context.MaintenanceHistoryEvents.AnyAsync(value => value.MaintenanceId == work.Id));
        Assert.False(await context.VehicleHistoryEvents.AnyAsync(value => value.VehicleId == work.VehicleId));
    }

    private static MaintenanceTestScope CreateService(OpenCRMContext context, IVehicleCounterCommandService counter)
    {
        var clock = new Mock<ISystemClock>();
        clock.SetupGet(value => value.UtcNow).Returns(new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc));
        var consumption = new Mock<IMaintenanceConsumptionService>();
        consumption.Setup(value => value.SynchronizeAsync(
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<IReadOnlyCollection<MaintenanceMaterialConsumptionLine>>()))
            .ReturnsAsync(new MaintenanceConsumptionResult(0, []));

        return new MaintenanceTestScope(
            new MaintenanceRepository(context),
            counter,
            clock.Object,
            context,
            Mock.Of<IOrganizationSubscriptionService>(),
            Mock.Of<IMaintenanceQueryService>(),
            Mock.Of<IMaintenanceSlotQueryService>(),
            consumption.Object);
    }

    private static Mock<IVehicleCounterCommandService> CreateFailingCounter(
        OpenCRMContext context,
        string userId,
        Guid vehicleId)
    {
        var counter = new Mock<IVehicleCounterCommandService>(MockBehavior.Strict);
        counter.Setup(value => value.SyncCounterFromMaintenanceAsync(
                userId,
                null,
                vehicleId,
                It.IsAny<Guid>(),
                It.IsAny<int?>(),
                It.IsAny<DateTime>()))
            .Returns((string _, Guid? _, Guid _, Guid _, int? _, DateTime _) =>
            {
                Assert.NotNull(context.Database.CurrentTransaction);
                return Task.FromException(new InvalidOperationException("Сбой синхронизации счётчика."));
            });

        return counter;
    }

    private static Vehicle CreateVehicle()
    {
        var user = new ApplicationUser
        {
            Email = $"personal-{Guid.NewGuid():N}@example.test",
            UserName = $"personal-{Guid.NewGuid():N}"
        };

        return new Vehicle
        {
            User = user,
            RegistrationPlate = "А123АА154"
        };
    }

    private static MaintenanceEntity CreateWork()
    {
        var vehicle = MaintenancePersonalAtomicityTests.CreateVehicle();
        var date = new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc);

        return new MaintenanceEntity
        {
            Vehicle = vehicle,
            User = vehicle.User,
            Status = MaintenanceStatus.Planned,
            Type = MaintenanceType.Maintenance,
            Date = date,
            PlannedDate = date.AddDays(1),
            DateTimeCreation = date.AddDays(-1),
            Comment = "Исходный комментарий",
            Details = [new() { Name = "Полировка", Price = 100 }]
        };
    }

    private static CreateMaintenanceDto CreateRequest()
    {
        var date = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc);

        return new CreateMaintenanceDto
        {
            Status = MaintenanceStatus.Planned,
            Type = MaintenanceType.Maintenance,
            PlannedDate = date,
            PlannedStartTime = TimeSpan.FromHours(9),
            PlannedEndTime = TimeSpan.FromHours(10),
            CompletedDate = null,
            Location = null,
            Performer = null,
            Mileage = null,
            Comment = "Личное обслуживание",
            Details = [new CreateMaintenanceDetailDto { Name = "Полировка", Price = 100 }]
        };
    }

    private static UpdateMaintenanceDto CreateUpdateRequest(MaintenanceEntity work)
    {
        return new UpdateMaintenanceDto
        {
            Status = work.Status,
            Type = work.Type,
            PlannedDate = work.PlannedDate,
            PlannedStartTime = work.PlannedStartTime,
            PlannedEndTime = work.PlannedEndTime,
            PlannedEndDate = work.PlannedEndDate,
            CompletedDate = work.CompletedDate,
            Location = work.Location,
            Performer = work.Performer,
            Mileage = work.Mileage,
            Comment = work.Comment,
            DiscountAmount = work.DiscountAmount,
            PaidAmount = work.PaidAmount,
            LaborCostAmount = work.LaborCostAmount,
            Details = work.Details.Select(detail => new CreateMaintenanceDetailDto
            {
                DetailId = detail.DetailId,
                Name = detail.Name,
                Description = detail.Description,
                Price = detail.Price,
                VatRateCode = detail.VatRateCode,
                EmployeeId = detail.EmployeeId,
                EmployeeName = detail.EmployeeName,
                CatalogItemId = detail.CatalogItemId,
                Quantity = detail.Quantity
            }).ToArray()
        };
    }
}
