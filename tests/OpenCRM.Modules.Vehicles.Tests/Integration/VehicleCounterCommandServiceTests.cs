using Microsoft.EntityFrameworkCore;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Modules.Vehicles;
using OpenCRM.Modules.Vehicles.Counters;
using OpenCRM.Infrastructure.Data.Repositories;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Modules.Vehicles.Contracts.Dtos;

namespace OpenCRM.Modules.Vehicles.Tests.Integration;

public sealed class VehicleCounterCommandServiceTests(VehicleDatabaseFixture fixture) : IClassFixture<VehicleDatabaseFixture>
{
    [Fact]
    public async Task ManualCounterUpdateAndDeletePreserveEntryHistoryAndRecalculateAfterDelete()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var vehicle = new Vehicle { User = user, UserId = user.Id };
        var firstRecordedAt = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
        var latestRecordedAt = firstRecordedAt.AddDays(1);
        var firstEntry = new VehicleCounterEntry
        {
            Vehicle = vehicle,
            UserId = user.Id,
            Value = 100,
            RecordedAt = firstRecordedAt,
            SourceType = VehicleCounterSourceTypes.Manual
        };
        var latestEntry = new VehicleCounterEntry
        {
            Vehicle = vehicle,
            UserId = user.Id,
            Value = 200,
            RecordedAt = latestRecordedAt,
            SourceType = VehicleCounterSourceTypes.Manual
        };
        vehicle.CurrentMileage = latestEntry.Value;
        vehicle.CurrentMileageUpdatedAt = latestEntry.RecordedAt;
        context.Users.Add(user);
        context.Vehicles.Add(vehicle);
        context.VehicleCounterEntries.AddRange(firstEntry, latestEntry);
        await context.SaveChangesAsync();

        var service = VehicleCounterCommandServiceTests.CreateService(context);
        await service.UpdateCounterAsync(user.Id, null, new UpdateVehicleCounterDto
        {
            VehicleId = vehicle.Id,
            EntryId = latestEntry.Id,
            Value = 250,
            RecordedAt = latestRecordedAt.AddHours(1),
            Comment = "  Проверка  "
        });

        Assert.Equal(250, vehicle.CurrentMileage);
        Assert.Equal(latestRecordedAt.AddHours(1), vehicle.CurrentMileageUpdatedAt);
        Assert.Equal("Проверка", latestEntry.Comment);
        var updateEvent = await context.VehicleHistoryEvents.SingleAsync(history =>
            history.VehicleId == vehicle.Id && history.EventType == "counter_updated");
        Assert.Equal("Значение: 200 км от 02.10.2026 -> 250 км от 02.10.2026", updateEvent.Description);

        await service.DeleteCounterEntryAsync(user.Id, null, latestEntry.Id);

        Assert.True(latestEntry.IsDeleted);
        Assert.Equal(firstEntry.Value, vehicle.CurrentMileage);
        Assert.Equal(firstEntry.RecordedAt, vehicle.CurrentMileageUpdatedAt);
        Assert.Contains(await context.VehicleHistoryEvents.ToListAsync(), history =>
            history.VehicleId == vehicle.Id && history.EventType == "counter_deleted");
    }

    [Fact]
    public async Task SameDayCounterUpdateReusesEntryAndRejectsConflictsAndNegativeValues()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var vehicle = new Vehicle { User = user, UserId = user.Id };
        var recordedAt = new DateTime(2026, 10, 5, 8, 0, 0, DateTimeKind.Utc);
        var entry = new VehicleCounterEntry
        {
            Vehicle = vehicle,
            UserId = user.Id,
            Value = 100,
            RecordedAt = recordedAt,
            SourceType = VehicleCounterSourceTypes.Manual
        };
        context.Users.Add(user);
        context.Vehicles.Add(vehicle);
        context.VehicleCounterEntries.Add(entry);
        await context.SaveChangesAsync();

        var service = VehicleCounterCommandServiceTests.CreateService(context);
        await service.UpdateCounterAsync(user.Id, null, new UpdateVehicleCounterDto
        {
            VehicleId = vehicle.Id,
            Value = 125,
            RecordedAt = recordedAt.AddHours(4)
        });

        Assert.Equal(1, await context.VehicleCounterEntries.CountAsync(counter => counter.VehicleId == vehicle.Id));
        Assert.Equal(125, entry.Value);
        Assert.Equal(recordedAt.AddHours(4), entry.RecordedAt);

        var conflictingEntry = new VehicleCounterEntry
        {
            VehicleId = vehicle.Id,
            UserId = user.Id,
            Value = 150,
            RecordedAt = recordedAt.AddHours(6),
            SourceType = VehicleCounterSourceTypes.Manual
        };
        context.VehicleCounterEntries.Add(conflictingEntry);
        await context.SaveChangesAsync();

        var conflictException = await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateCounterAsync(
            user.Id,
            null,
            new UpdateVehicleCounterDto
            {
                VehicleId = vehicle.Id,
                EntryId = entry.Id,
                Value = 175,
                RecordedAt = recordedAt.AddHours(7)
            }));
        var negativeValueException = await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateCounterAsync(
            user.Id,
            null,
            new UpdateVehicleCounterDto
            {
                VehicleId = vehicle.Id,
                Value = -1,
                RecordedAt = recordedAt.AddDays(1)
            }));

        Assert.Equal("На эту дату уже есть другая ручная запись.", conflictException.Message);
        Assert.Equal("Значение счетчика не может быть отрицательным.", negativeValueException.Message);
        Assert.Equal(125, entry.Value);
        Assert.Equal(150, conflictingEntry.Value);
        Assert.Single(await context.VehicleHistoryEvents.Where(history =>
            history.VehicleId == vehicle.Id && history.EventType == "counter_updated").ToListAsync());
    }

    [Fact]
    public async Task CounterDeleteRejectsForeignAndMaintenanceEntries()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var foreignUser = new ApplicationUser();
        var vehicle = new Vehicle { User = foreignUser, UserId = foreignUser.Id };
        var foreignEntry = new VehicleCounterEntry
        {
            Vehicle = vehicle,
            UserId = foreignUser.Id,
            Value = 100,
            RecordedAt = DateTime.UtcNow,
            SourceType = VehicleCounterSourceTypes.Manual
        };
        var maintenanceEntry = new VehicleCounterEntry
        {
            Vehicle = vehicle,
            UserId = foreignUser.Id,
            Value = 200,
            RecordedAt = DateTime.UtcNow.AddDays(1),
            SourceType = VehicleCounterSourceTypes.Maintenance
        };
        context.Users.AddRange(user, foreignUser);
        context.Vehicles.Add(vehicle);
        context.VehicleCounterEntries.AddRange(foreignEntry, maintenanceEntry);
        await context.SaveChangesAsync();

        var service = VehicleCounterCommandServiceTests.CreateService(context);
        var foreignException = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteCounterEntryAsync(
            user.Id,
            null,
            foreignEntry.Id));
        var maintenanceException = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteCounterEntryAsync(
            foreignUser.Id,
            null,
            maintenanceEntry.Id));

        Assert.Equal("Ручная запись не найдена или уже удалена.", foreignException.Message);
        Assert.Equal("Ручная запись не найдена или уже удалена.", maintenanceException.Message);
        Assert.False(foreignEntry.IsDeleted);
        Assert.False(maintenanceEntry.IsDeleted);
        Assert.Empty(await context.VehicleHistoryEvents.Where(history => history.VehicleId == vehicle.Id).ToListAsync());
    }

    [Fact]
    public async Task MaintenanceCounterSyncUpdatesInPlacePreservesNewestReadingAndCanBeRemoved()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var vehicle = new Vehicle { User = user, UserId = user.Id };
        var earlierDate = new DateTime(2026, 10, 7, 8, 0, 0, DateTimeKind.Utc);
        var currentDate = earlierDate.AddDays(1);
        var newerDate = currentDate.AddDays(1);
        var maintenance = new OpenCRM.Domain.Maintenance
        {
            Vehicle = vehicle,
            UserId = user.Id,
            Date = currentDate,
            DateTimeCreation = currentDate,
            Status = MaintenanceStatus.Completed,
            Type = MaintenanceType.Maintenance
        };
        var baselineEntry = new VehicleCounterEntry
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Vehicle = vehicle,
            UserId = user.Id,
            Value = 200,
            RecordedAt = currentDate,
            SourceType = VehicleCounterSourceTypes.Manual
        };
        vehicle.CurrentMileage = baselineEntry.Value;
        vehicle.CurrentMileageUpdatedAt = baselineEntry.RecordedAt;
        context.Users.Add(user);
        context.Vehicles.Add(vehicle);
        context.Maintenance.Add(maintenance);
        context.VehicleCounterEntries.Add(baselineEntry);
        await context.SaveChangesAsync();

        var service = VehicleCounterCommandServiceTests.CreateService(context);
        await service.SyncCounterFromMaintenanceAsync(user.Id, null, vehicle.Id, maintenance.Id, 150, earlierDate);
        Assert.Equal(200, vehicle.CurrentMileage);
        Assert.Equal(currentDate, vehicle.CurrentMileageUpdatedAt);

        await service.SyncCounterFromMaintenanceAsync(user.Id, null, vehicle.Id, maintenance.Id, 250, currentDate);
        Assert.Equal(250, vehicle.CurrentMileage);
        Assert.Equal(currentDate, vehicle.CurrentMileageUpdatedAt);

        await service.SyncCounterFromMaintenanceAsync(user.Id, null, vehicle.Id, maintenance.Id, 300, newerDate);
        Assert.Equal(300, vehicle.CurrentMileage);
        Assert.Equal(newerDate, vehicle.CurrentMileageUpdatedAt);

        await service.SyncCounterFromMaintenanceAsync(user.Id, null, vehicle.Id, maintenance.Id, 125, earlierDate);
        Assert.Equal(200, vehicle.CurrentMileage);
        Assert.Equal(currentDate, vehicle.CurrentMileageUpdatedAt);
        var syncedEntries = await context.VehicleCounterEntries
            .Where(entry => entry.MaintenanceId == maintenance.Id)
            .ToListAsync();
        var syncedEntry = Assert.Single(syncedEntries);
        Assert.Equal(125, syncedEntry.Value);

        await service.SyncCounterFromMaintenanceAsync(user.Id, null, vehicle.Id, maintenance.Id, null, earlierDate);
        Assert.True(syncedEntry.IsDeleted);
        Assert.Equal(200, vehicle.CurrentMileage);
        Assert.Equal(currentDate, vehicle.CurrentMileageUpdatedAt);

        await service.RemoveCounterSyncFromMaintenanceAsync(user.Id, null, vehicle.Id, maintenance.Id);
        Assert.Equal(200, vehicle.CurrentMileage);
        Assert.Equal(currentDate, vehicle.CurrentMileageUpdatedAt);
    }

    [Fact]
    public async Task CounterRecalculationUsesIdToBreakEqualTimestampTies()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var vehicle = new Vehicle { User = user, UserId = user.Id };
        var recordedAt = new DateTime(2026, 10, 9, 8, 0, 0, DateTimeKind.Utc);
        var firstId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var secondId = Guid.Parse("10000000-0000-0000-0000-000000000002");
        var firstEntry = new VehicleCounterEntry
        {
            Id = firstId,
            Vehicle = vehicle,
            UserId = user.Id,
            Value = 100,
            RecordedAt = recordedAt,
            SourceType = VehicleCounterSourceTypes.Manual
        };
        var secondEntry = new VehicleCounterEntry
        {
            Id = secondId,
            Vehicle = vehicle,
            UserId = user.Id,
            Value = 200,
            RecordedAt = recordedAt,
            SourceType = VehicleCounterSourceTypes.Manual
        };
        vehicle.CurrentMileage = secondEntry.Value;
        vehicle.CurrentMileageUpdatedAt = recordedAt;
        context.Users.Add(user);
        context.Vehicles.Add(vehicle);
        context.VehicleCounterEntries.AddRange(firstEntry, secondEntry);
        await context.SaveChangesAsync();

        await VehicleCounterCommandServiceTests.CreateService(context).DeleteCounterEntryAsync(user.Id, null, secondEntry.Id);

        Assert.Equal(firstEntry.Value, vehicle.CurrentMileage);
        Assert.Equal(recordedAt, vehicle.CurrentMileageUpdatedAt);
    }

    [Fact]
    public async Task MaintenanceCounterSyncUsesEntryIdToBreakEqualTimestampTies()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var vehicle = new Vehicle { User = user, UserId = user.Id };
        var recordedAt = new DateTime(2026, 10, 9, 10, 0, 0, DateTimeKind.Utc);
        var firstMaintenance = new OpenCRM.Domain.Maintenance
        {
            Vehicle = vehicle,
            UserId = user.Id,
            Date = recordedAt,
            DateTimeCreation = recordedAt,
            Status = MaintenanceStatus.Completed,
            Type = MaintenanceType.Maintenance
        };
        var secondMaintenance = new OpenCRM.Domain.Maintenance
        {
            Vehicle = vehicle,
            UserId = user.Id,
            Date = recordedAt,
            DateTimeCreation = recordedAt,
            Status = MaintenanceStatus.Completed,
            Type = MaintenanceType.Maintenance
        };
        var firstEntry = new VehicleCounterEntry
        {
            Id = Guid.Parse("20000000-0000-0000-0000-000000000001"),
            Vehicle = vehicle,
            UserId = user.Id,
            Maintenance = firstMaintenance,
            Value = 100,
            RecordedAt = recordedAt,
            SourceType = VehicleCounterSourceTypes.Maintenance
        };
        var secondEntry = new VehicleCounterEntry
        {
            Id = Guid.Parse("20000000-0000-0000-0000-000000000002"),
            Vehicle = vehicle,
            UserId = user.Id,
            Maintenance = secondMaintenance,
            Value = 200,
            RecordedAt = recordedAt,
            SourceType = VehicleCounterSourceTypes.Maintenance
        };
        vehicle.CurrentMileage = secondEntry.Value;
        vehicle.CurrentMileageUpdatedAt = recordedAt;
        context.Users.Add(user);
        context.Vehicles.Add(vehicle);
        context.Maintenance.AddRange(firstMaintenance, secondMaintenance);
        context.VehicleCounterEntries.AddRange(firstEntry, secondEntry);
        await context.SaveChangesAsync();

        await VehicleCounterCommandServiceTests.CreateService(context).SyncCounterFromMaintenanceAsync(
            user.Id,
            null,
            vehicle.Id,
            secondMaintenance.Id,
            null,
            recordedAt);

        Assert.True(secondEntry.IsDeleted);
        Assert.Equal(firstEntry.Value, vehicle.CurrentMileage);
        Assert.Equal(recordedAt, vehicle.CurrentMileageUpdatedAt);
    }

    [Fact]
    public async Task OrganizationCounterCommandsRequireCounterManagementPermission()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var organizationId = Guid.NewGuid();
        var subscriptions = new Mock<IOrganizationSubscriptionService>();
        subscriptions
            .Setup(service => service.EnsureWorkspaceCanWriteAsync(
                user.Id,
                organizationId,
                OrganizationPermissionCodes.CountersManage))
            .ThrowsAsync(new UnauthorizedAccessException("Нет права управления счетчиками."));
        var vehicleRepository = new VehicleRepository(context);
        var clock = new Mock<ISystemClock>();
        var service = new VehicleCounterCommandService(
            vehicleRepository,
            clock.Object,
            context,
            subscriptions.Object,
            new VehicleHistoryWriter(context, clock.Object, vehicleRepository, subscriptions.Object));

        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.UpdateCounterAsync(
            user.Id,
            organizationId,
            new UpdateVehicleCounterDto { VehicleId = Guid.NewGuid(), Value = 100 }));

        Assert.Equal("Нет права управления счетчиками.", exception.Message);
        subscriptions.Verify(service => service.EnsureWorkspaceCanWriteAsync(
            user.Id,
            organizationId,
            OrganizationPermissionCodes.CountersManage),
            Times.Once);
    }

    [Fact]
    public async Task MaintenanceCounterSynchronizationParticipatesInTheCallerTransaction()
    {
        var user = new ApplicationUser();
        var vehicleId = Guid.NewGuid();
        var maintenanceId = Guid.NewGuid();
        var recordedAt = new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);

        await using (var seedContext = fixture.CreateContext())
        {
            var vehicle = new Vehicle { Id = vehicleId, User = user, UserId = user.Id };
            seedContext.Users.Add(user);
            seedContext.Vehicles.Add(vehicle);
            seedContext.Maintenance.Add(new OpenCRM.Domain.Maintenance
            {
                Id = maintenanceId,
                Vehicle = vehicle,
                VehicleId = vehicle.Id,
                UserId = user.Id,
                Date = recordedAt,
                DateTimeCreation = recordedAt,
                Status = MaintenanceStatus.Completed,
                Type = MaintenanceType.Maintenance
            });
            await seedContext.SaveChangesAsync();
        }

        await using (var context = fixture.CreateContext())
        {
            var executionStrategy = context.Database.CreateExecutionStrategy();
            await executionStrategy.ExecuteAsync(async () =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync();
                var service = VehicleCounterCommandServiceTests.CreateService(context);
                await service.SyncCounterFromMaintenanceAsync(user.Id, null, vehicleId, maintenanceId, 54321, recordedAt);
                Assert.Equal(54321, await context.Vehicles.Where(vehicle => vehicle.Id == vehicleId).Select(vehicle => vehicle.CurrentMileage).SingleAsync());
                await transaction.RollbackAsync();
            });
        }

        await using (var verificationContext = fixture.CreateContext())
        {
            Assert.Empty(await verificationContext.VehicleCounterEntries.Where(entry => entry.MaintenanceId == maintenanceId).ToListAsync());
            Assert.Null(await verificationContext.Vehicles.Where(vehicle => vehicle.Id == vehicleId).Select(vehicle => vehicle.CurrentMileage).SingleAsync());
        }

        await using (var context = fixture.CreateContext())
        {
            var service = VehicleCounterCommandServiceTests.CreateService(context);
            await service.SyncCounterFromMaintenanceAsync(user.Id, null, vehicleId, maintenanceId, 54321, recordedAt);
            Assert.Equal(54321, await context.Vehicles.Where(vehicle => vehicle.Id == vehicleId).Select(vehicle => vehicle.CurrentMileage).SingleAsync());
        }

        await using var finalContext = fixture.CreateContext();
        var savedEntry = await finalContext.VehicleCounterEntries.SingleAsync(entry => entry.MaintenanceId == maintenanceId);
        Assert.Equal(54321, savedEntry.Value);
        Assert.Equal(VehicleCounterSourceTypes.Maintenance, savedEntry.SourceType);
    }

    [Fact]
    public async Task CounterGuardsRejectMissingNegativeForeignAndConflictingEntries()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var vehicle = new Vehicle { User = user, MileageUnit = VehicleMileageUnit.EngineHours };
        var first = new VehicleCounterEntry { Vehicle = vehicle, User = user, Value = 10, RecordedAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc) };
        var second = new VehicleCounterEntry { Vehicle = vehicle, User = user, Value = 20, RecordedAt = first.RecordedAt.AddDays(1) };
        context.AddRange(first, second);
        await context.SaveChangesAsync();
        var service = VehicleCounterCommandServiceTests.CreateService(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateCounterAsync(user.Id, null,
            new UpdateVehicleCounterDto { VehicleId = Guid.NewGuid(), Value = 10 }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateCounterAsync(user.Id, null,
            new UpdateVehicleCounterDto { VehicleId = vehicle.Id, Value = -1 }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateCounterAsync(user.Id, null,
            new UpdateVehicleCounterDto { VehicleId = vehicle.Id, EntryId = Guid.NewGuid(), Value = 10 }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateCounterAsync(user.Id, null,
            new UpdateVehicleCounterDto { VehicleId = vehicle.Id, EntryId = first.Id, Value = 30, RecordedAt = second.RecordedAt }));
        await service.UpdateCounterAsync(user.Id, null, new UpdateVehicleCounterDto { VehicleId = vehicle.Id, Value = 40, Comment = " " });
        var latest = await context.VehicleCounterEntries.SingleAsync(entry => entry.VehicleId == vehicle.Id && entry.Value == 40);
        Assert.Equal(new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc), latest.RecordedAt);
        Assert.Null(latest.Comment);
        await service.UpdateCounterAsync(user.Id, null, new UpdateVehicleCounterDto { VehicleId = vehicle.Id, Value = 45 });
        await service.DeleteCounterEntryAsync(user.Id, null, latest.Id);
        Assert.Equal(20, vehicle.CurrentMileage);
        var history = await context.VehicleHistoryEvents.Where(entry => entry.VehicleId == vehicle.Id).ToListAsync();
        Assert.Contains(history, entry => entry.EventType == "counter_updated" && entry.Description!.Contains("м/ч"));
        Assert.Contains(history, entry => entry.EventType == "counter_deleted" && entry.Description!.Contains("удалено"));
    }

    [Fact]
    public async Task MaintenanceSyncHandlesAbsentValueNegativeValueAndExplicitRemoval()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var vehicle = new Vehicle { User = user };
        var work = new OpenCRM.Domain.Maintenance
        {
            User = user, Vehicle = vehicle, Date = DateTime.UtcNow, DateTimeCreation = DateTime.UtcNow,
            Status = MaintenanceStatus.Completed, Type = MaintenanceType.Maintenance
        };
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        var service = VehicleCounterCommandServiceTests.CreateService(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SyncCounterFromMaintenanceAsync(user.Id, null,
            Guid.NewGuid(), work.Id, 100, work.Date));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RemoveCounterSyncFromMaintenanceAsync(user.Id, null,
            Guid.NewGuid(), work.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SyncCounterFromMaintenanceAsync(user.Id, null,
            vehicle.Id, work.Id, -1, work.Date));
        await service.SyncCounterFromMaintenanceAsync(user.Id, null, vehicle.Id, work.Id, null, work.Date);
        await service.RemoveCounterSyncFromMaintenanceAsync(user.Id, null, vehicle.Id, work.Id);
        Assert.Null(vehicle.CurrentMileage);
        await service.SyncCounterFromMaintenanceAsync(user.Id, null, vehicle.Id, work.Id, 100, work.Date);
        await service.RemoveCounterSyncFromMaintenanceAsync(user.Id, null, vehicle.Id, work.Id);

        Assert.Null(vehicle.CurrentMileage);
        Assert.Null(vehicle.CurrentMileageUpdatedAt);
        Assert.True((await context.VehicleCounterEntries.SingleAsync(entry => entry.MaintenanceId == work.Id)).IsDeleted);
    }

    [Fact]
    public async Task RecalculationMergesPendingPhysicalDeletionWithPersistedEntries()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var vehicle = new Vehicle { User = user };
        var entry = new VehicleCounterEntry { Vehicle = vehicle, User = user, Value = 999, RecordedAt = DateTime.UtcNow.AddDays(1) };
        var other = new VehicleCounterEntry { Vehicle = new Vehicle { User = user }, User = user, Value = 888, RecordedAt = entry.RecordedAt };
        context.AddRange(entry, other);
        await context.SaveChangesAsync();
        context.VehicleCounterEntries.Remove(entry);
        other.Comment = "Несохранённое изменение другой техники";
        var service = VehicleCounterCommandServiceTests.CreateService(context);
        await service.UpdateCounterAsync(user.Id, null, new UpdateVehicleCounterDto
        {
            VehicleId = vehicle.Id, Value = 100, RecordedAt = entry.RecordedAt.AddDays(-1)
        });

        Assert.Equal(100, vehicle.CurrentMileage);
        Assert.False(await context.VehicleCounterEntries.AnyAsync(item => item.Id == entry.Id));
    }

    private static VehicleCounterCommandService CreateService(OpenCRM.Infrastructure.Data.OpenCRMContext context)
    {
        var clock = new Mock<ISystemClock>();
        clock.SetupGet(value => value.UtcNow).Returns(new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc));
        var vehicleRepository = new VehicleRepository(context);
        var subscriptions = Mock.Of<IOrganizationSubscriptionService>();
        var historyWriter = new VehicleHistoryWriter(context, clock.Object, vehicleRepository, subscriptions);

        return new VehicleCounterCommandService(vehicleRepository, clock.Object, context, subscriptions, historyWriter);
    }
}
