using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Modules.Maintenance.Work;

namespace OpenCRM.Modules.Maintenance.Tests.Unit;

public sealed class MaintenanceWorkMappingTests
{
    [Fact]
    public void AssignmentUsesTheDetailEmployeeThenTheOrderEmployeeAndAllowsUnassignedWork()
    {
        var employeeId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();
        var work = new MaintenanceEntity { EmployeeId = employeeId };
        var detail = new MaintenanceDetail();

        Assert.True(MaintenanceWorkMapping.IsAssignedTo(work, detail, employeeId));
        Assert.False(MaintenanceWorkMapping.IsAssignedTo(work, detail, otherEmployeeId));
        detail.EmployeeId = otherEmployeeId;
        Assert.False(MaintenanceWorkMapping.IsAssignedTo(work, detail, employeeId));
        Assert.True(MaintenanceWorkMapping.IsAssignedTo(work, detail, otherEmployeeId));
        detail.EmployeeId = null;
        work.EmployeeId = null;
        Assert.False(MaintenanceWorkMapping.IsAssignedTo(work, detail, employeeId));
        Assert.False(MaintenanceWorkMapping.IsAssignedTo(work, detail, Guid.Empty));
    }

    [Fact]
    public void IsMaterialRecognizesCatalogNameAndQuantityFields()
    {
        Assert.True(MaintenanceWorkMapping.IsMaterial(new MaintenanceDetail
        {
            MaterialCatalogItemId = Guid.NewGuid()
        }));
        Assert.True(MaintenanceWorkMapping.IsMaterial(new MaintenanceDetail
        {
            MaterialName = "Масло"
        }));
        Assert.True(MaintenanceWorkMapping.IsMaterial(new MaintenanceDetail
        {
            MaterialQuantity = 1
        }));
        Assert.False(MaintenanceWorkMapping.IsMaterial(new MaintenanceDetail()));
    }

    [Fact]
    public void MapItemCalculatesElapsedIntervalsAndUsesOperationState()
    {
        var now = new DateTime(2026, 10, 11, 9, 0, 0, DateTimeKind.Utc);
        var maintenance = CreateMaintenance();
        maintenance.CurrencyCode = CurrencyCodes.Cny;
        var detail = new MaintenanceDetail
        {
            DetailId = Guid.NewGuid(),
            Name = "Полировка",
            Description = "Капот",
            Price = 1200,
            Quantity = 2
        };
        var operation = new MaintenanceWorkOperation
        {
            Id = detail.DetailId,
            Status = MaintenanceWorkStatus.Paused,
            Version = 4,
            StartedAt = now.AddHours(-1)
        };
        var intervals = new[]
        {
            new MaintenanceWorkInterval { StartedAt = now.AddHours(-1), EndedAt = now.AddMinutes(-50) },
            new MaintenanceWorkInterval { StartedAt = now.AddMinutes(-40) },
            new MaintenanceWorkInterval { StartedAt = now.AddMinutes(-20), EndedAt = now.AddMinutes(10) },
            new MaintenanceWorkInterval { StartedAt = now.AddMinutes(5) }
        };

        var result = MaintenanceWorkMapping.MapItem(
            maintenance, detail, operation, intervals, now, adjustmentMinutes: -5, reworkReason: "Перепроверить");

        Assert.Equal(maintenance.Id, result.MaintenanceId);
        Assert.Equal(CurrencyCodes.Cny, result.CurrencyCode);
        Assert.Equal(detail.DetailId, result.DetailId);
        Assert.Equal("Полировка", result.Name);
        Assert.Equal("Автомобиль", result.VehicleName);
        Assert.Equal("А123АА", result.RegistrationPlate);
        Assert.Equal(MaintenanceWorkStatus.Paused, result.Status);
        Assert.Equal(4u, result.Version);
        Assert.Equal(now.AddHours(-1), result.StartedAt);
        Assert.Null(result.CompletedAt);
        Assert.Equal(TimeSpan.FromMinutes(65), result.ActiveTime);
        Assert.Equal("Перепроверить", result.ReworkReason);
    }

    [Fact]
    public void MapItemUsesExplicitCompletionAndVersionOverrides()
    {
        var now = new DateTime(2026, 10, 11, 9, 0, 0, DateTimeKind.Utc);
        var maintenance = CreateMaintenance();
        var detail = new MaintenanceDetail { DetailId = Guid.NewGuid(), Name = "Мойка" };

        var result = MaintenanceWorkMapping.MapItem(
            maintenance, detail, null, [], now,
            status: MaintenanceWorkStatus.Completed,
            version: 5);

        Assert.Equal(MaintenanceWorkStatus.Completed, result.Status);
        Assert.Equal(5u, result.Version);
        Assert.Equal(now, result.CompletedAt);
        Assert.Equal(TimeSpan.Zero, result.ActiveTime);
    }

    [Fact]
    public void MapReportCopiesDecisionAndAdjustmentDetails()
    {
        var createdAt = new DateTime(2026, 10, 11, 9, 0, 0, DateTimeKind.Utc);
        var decidedAt = createdAt.AddMinutes(3);
        var report = new MaintenanceWorkReport
        {
            Id = Guid.NewGuid(),
            MaintenanceId = Guid.NewGuid(),
            DetailId = Guid.NewGuid(),
            Kind = "time",
            Text = "Пересмотрено",
            CreatedAt = createdAt,
            Decision = "approved",
            DecisionReason = "Подтверждено",
            DecidedAt = decidedAt,
            ApprovedPrice = 900,
            TimeAdjustmentMinutes = 15
        };

        var result = MaintenanceWorkMapping.MapReport(report, "Мастер");

        Assert.Equal(report.Id, result.Id);
        Assert.Equal(report.MaintenanceId, result.MaintenanceId);
        Assert.Equal(report.DetailId, result.DetailId);
        Assert.Equal("Мастер", result.EmployeeName);
        Assert.Equal(createdAt, result.CreatedAt);
        Assert.Equal("approved", result.Decision);
        Assert.Equal("Подтверждено", result.DecisionReason);
        Assert.Equal(decidedAt, result.DecidedAt);
        Assert.Equal(900, result.ApprovedPrice);
        Assert.Equal(15, result.TimeAdjustmentMinutes);
    }

    [Fact]
    public void CompletedItemWithoutAnOperationHasNoInventedCompletionTimestamp()
    {
        var result = MaintenanceWorkMapping.MapItem(
            CreateMaintenance(),
            new MaintenanceDetail(),
            null,
            [],
            DateTime.UtcNow);

        Assert.Equal(MaintenanceWorkStatus.Assigned, result.Status);
        Assert.Equal(0u, result.Version);
        Assert.Null(result.CompletedAt);

        var operation = new MaintenanceWorkOperation { Status = MaintenanceWorkStatus.Completed };
        var completed = MaintenanceWorkMapping.MapItem(
            CreateMaintenance(), new MaintenanceDetail(), operation, [], DateTime.UtcNow);

        Assert.Null(completed.CompletedAt);
    }

    private static MaintenanceEntity CreateMaintenance() => new()
    {
        Id = Guid.NewGuid(),
        Vehicle = new Vehicle { Name = "Автомобиль", RegistrationPlate = "А123АА" },
        Location = "Пост 1",
        IntakeNotes = "Принят чистым",
        IntakeCompleteness = "Комплект",
        IntakeVisibleDamage = "Нет",
        Comment = "Осмотр"
    };
}
