using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Modules.Maintenance;

namespace OpenCRM.Modules.Maintenance.Tests.Unit;

public sealed class MaintenanceSnapshotTests
{
    [Fact]
    public void FromCapturesNormalizedOrderAndCompleteDetailValues()
    {
        var detailId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var catalogItemId = Guid.NewGuid();
        var materialCatalogItemId = Guid.NewGuid();
        var date = new DateTime(2026, 10, 11, 23, 45, 0, DateTimeKind.Local);
        var maintenance = new MaintenanceEntity
        {
            Status = MaintenanceStatus.Completed,
            Type = MaintenanceType.Repair,
            PlannedDate = date,
            PlannedEndDate = date.AddDays(1),
            PlannedStartTime = TimeSpan.FromHours(9),
            PlannedEndTime = TimeSpan.FromHours(10),
            CompletedDate = date.AddDays(2),
            ReadyForHandoverAt = date,
            HandedOverAt = date.AddHours(1),
            Location = "Пост 1",
            EmployeeId = employeeId,
            Performer = "Мастер",
            Mileage = 12345,
            Comment = "Комментарий",
            DiscountAmount = 10,
            PaidAmount = 100,
            LaborCostAmount = 20,
            Details =
            [
                new MaintenanceDetail
                {
                    DetailId = detailId,
                    EmployeeId = employeeId,
                    EmployeeName = "Мастер",
                    CatalogItemId = catalogItemId,
                    Name = "Полировка",
                    Description = "Капот",
                    Price = 1200,
                    VatRateCode = MaintenanceVatRates.WithoutVat,
                    Quantity = 2,
                    MaterialCatalogItemId = materialCatalogItemId,
                    MaterialUnit = "л",
                    MaterialTotalCost = 30,
                    MaterialName = "Полироль",
                    MaterialQuantity = 0.5m,
                    MaterialUnitCost = 60
                }
            ]
        };

        var snapshot = MaintenanceSnapshot.From(maintenance);

        Assert.Equal(maintenance.Status, snapshot.Status);
        Assert.Equal(maintenance.Type, snapshot.Type);
        Assert.Equal(new DateTime(2026, 10, 11), snapshot.PlannedDate);
        Assert.Equal(new DateTime(2026, 10, 12), snapshot.PlannedEndDate);
        Assert.Equal(maintenance.PlannedStartTime, snapshot.PlannedStartTime);
        Assert.Equal(maintenance.PlannedEndTime, snapshot.PlannedEndTime);
        Assert.Equal(new DateTime(2026, 10, 13), snapshot.CompletedDate);
        Assert.Equal(maintenance.ReadyForHandoverAt, snapshot.ReadyForHandoverAt);
        Assert.Equal(maintenance.HandedOverAt, snapshot.HandedOverAt);
        Assert.Equal(maintenance.Location, snapshot.Location);
        Assert.Equal(employeeId, snapshot.EmployeeId);
        Assert.Equal(maintenance.Performer, snapshot.Performer);
        Assert.Equal(maintenance.Mileage, snapshot.Mileage);
        Assert.Equal(maintenance.Comment, snapshot.Comment);
        Assert.Equal(maintenance.DiscountAmount, snapshot.DiscountAmount);
        Assert.Equal(maintenance.PaidAmount, snapshot.PaidAmount);
        Assert.Equal(maintenance.LaborCostAmount, snapshot.LaborCostAmount);

        var savedDetail = Assert.Single(snapshot.Details);
        Assert.Equal(detailId, savedDetail.DetailId);
        Assert.Equal(employeeId, savedDetail.EmployeeId);
        Assert.Equal("Мастер", savedDetail.EmployeeName);
        Assert.Equal(catalogItemId, savedDetail.CatalogItemId);
        Assert.Equal("Полировка", savedDetail.Name);
        Assert.Equal("Капот", savedDetail.Description);
        Assert.Equal(1200, savedDetail.Price);
        Assert.Equal(MaintenanceVatRates.WithoutVat, savedDetail.VatRateCode);
        Assert.Equal(2, savedDetail.Quantity);
        Assert.Equal(materialCatalogItemId, savedDetail.MaterialCatalogItemId);
        Assert.Equal("л", savedDetail.MaterialUnit);
        Assert.Equal(30, savedDetail.MaterialTotalCost);
        Assert.Equal("Полироль", savedDetail.MaterialName);
        Assert.Equal(0.5m, savedDetail.MaterialQuantity);
        Assert.Equal(60, savedDetail.MaterialUnitCost);
        Assert.NotSame(maintenance.Details[0], savedDetail);
    }

    [Fact]
    public void FromReturnsAnEmptyDetailSnapshotWhenOrderHasNoLines()
    {
        var maintenance = new MaintenanceEntity();

        var snapshot = MaintenanceSnapshot.From(maintenance);

        Assert.Empty(snapshot.Details);
    }
}
