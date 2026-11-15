using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Modules.Workspace.Queries;
using MaintenanceEntity = OpenCRM.Domain.Maintenance;

namespace OpenCRM.Modules.Workspace.Tests.Unit;

public sealed class WorkspaceMaintenanceMapperTests
{
    [Theory]
    [InlineData(MaintenanceType.Maintenance, "detailing", "Уход / мойка")]
    [InlineData(MaintenanceType.Repair, "detailing", "Ремонт")]
    [InlineData(MaintenanceType.Refill, " DETAILING ", "Заправка")]
    [InlineData(MaintenanceType.Other, "detailing", "Другое")]
    [InlineData((MaintenanceType)99, "detailing", "Другое")]
    [InlineData(MaintenanceType.Maintenance, null, "Ремонт и обслуживание")]
    [InlineData(MaintenanceType.Repair, "service-station", "Плановое ТО")]
    [InlineData(MaintenanceType.Refill, "fleet", "Заправка топливом")]
    [InlineData(MaintenanceType.Other, "other", "Другое")]
    [InlineData((MaintenanceType)99, "fleet", "Другое")]
    public void TypeNamesPreserveScenarioSpecificLabels(MaintenanceType type, string? scenario, string expected)
    {
        Assert.Equal(expected, OrganizationWorkspaceMaintenanceMapper.GetMaintenanceTypeName(type, scenario));
    }

    [Theory]
    [InlineData(MaintenanceStatus.Planned, "Запланировано")]
    [InlineData(MaintenanceStatus.InProgress, "В работе")]
    [InlineData(MaintenanceStatus.Completed, "Выполнено")]
    [InlineData(MaintenanceStatus.Cancelled, "Отменено")]
    [InlineData((MaintenanceStatus)99, "Неизвестно")]
    public void StatusNamesPreserveUnknownHistoricalValues(MaintenanceStatus status, string expected)
    {
        Assert.Equal(expected, OrganizationWorkspaceMaintenanceMapper.GetMaintenanceStatusName(status));
    }

    [Theory]
    [InlineData("manual", "ManualBrand ManualModel", "Name • 2020 г. • Client • А123АА154")]
    [InlineData("empty", "Автомобиль", null)]
    [InlineData("missing-navigation", "Автомобиль · А123АА154", "2020 г. • Client • А123АА154")]
    [InlineData("catalog", "CatalogBrand CatalogModel", "2020 г. • Client • А123АА154")]
    [InlineData("brand-only", "CatalogBrand ManualModel", "2020 г. • Client • А123АА154")]
    [InlineData("model-only", "ManualBrand CatalogModel", "2020 г. • Client • А123АА154")]
    [InlineData("missing-brand", "CatalogModel", "2020 г. • Client • А123АА154")]
    [InlineData("missing-model", "CatalogBrand", "2020 г. • Client • А123АА154")]
    public void VehicleIdentityKeepsHistoricalCatalogIdsAndManualFallback(string identity, string displayName, string? subtitle)
    {
        var vehicle = new Vehicle
        {
            ManualBrandName = "ManualBrand",
            ManualModelName = "ManualModel",
            Name = "Name",
            YearIssue = 2020,
            RegistrationPlate = "А123АА154",
            OrganizationClient = new OrganizationClient { Name = "Client" }
        };
        if (identity is "catalog" or "missing-navigation" or "brand-only" or "missing-brand" or "missing-model")
        {
            vehicle.BrandId = 1;
            vehicle.Brand = identity is "missing-navigation" or "missing-brand" ? null : new Brand { Name = "CatalogBrand" };
        }
        if (identity is "catalog" or "missing-navigation" or "model-only" or "missing-brand" or "missing-model")
        {
            vehicle.ModelId = 2;
            vehicle.Model = identity is "missing-navigation" or "missing-model" ? null : new Model { Name = "CatalogModel" };
        }
        if (identity == "empty")
        {
            vehicle.ManualBrandName = null;
            vehicle.ManualModelName = null;
            vehicle.Name = null;
            vehicle.YearIssue = null;
            vehicle.RegistrationPlate = null;
            vehicle.OrganizationClient = null;
        }
        var maintenance = new MaintenanceEntity { Vehicle = vehicle, VehicleId = vehicle.Id };

        var result = OrganizationWorkspaceMaintenanceMapper.Map(maintenance);

        Assert.Equal(displayName, result.VehicleDisplayName);
        Assert.Equal(subtitle, result.VehicleSubtitle);
        Assert.Equal(vehicle.Id, result.VehicleId);
    }

    [Theory]
    [InlineData("actual", "Запланировано")]
    [InlineData("planned", "Готов к выдаче")]
    [InlineData("display", "Выдан")]
    public void DateHandoverAndOrderFieldsKeepTheirPrecedence(string basis, string expectedStatus)
    {
        var date = new DateTime(2026, 10, 10);
        var maintenance = new MaintenanceEntity
        {
            Version = 17,
            Vehicle = new Vehicle { MileageUnit = VehicleMileageUnit.EngineHours },
            Date = date,
            Status = MaintenanceStatus.Planned,
            PlannedDate = basis == "actual" ? null : date.AddDays(1),
            PlannedStartTime = TimeSpan.FromHours(9),
            PlannedEndDate = date.AddDays(2),
            PlannedEndTime = TimeSpan.FromHours(11),
            ReadyForHandoverAt = basis == "actual" ? null : date,
            HandedOverAt = basis == "display" ? date : null,
            Location = "Пост 1",
            Mileage = 315,
            Comment = "Комментарий",
            Details = [new() { Price = 50.25m }, new() { Price = null }, new() { Price = 19.75m }]
        };
        DateTime? displayDate = basis == "display" ? date.AddDays(3) : null;

        var result = OrganizationWorkspaceMaintenanceMapper.Map(maintenance, displayDate);

        Assert.Equal(maintenance.Id, result.Id);
        Assert.Equal(17u, result.Version);
        Assert.Equal(expectedStatus, result.StatusName);
        Assert.Equal(MaintenanceStatus.Planned, result.Status);
        Assert.Equal(basis != "actual", result.IsPlannedDate);
        Assert.Equal(basis == "display" ? date.AddDays(3) : basis == "planned" ? date.AddDays(1) : date, result.Date);
        Assert.Equal(maintenance.PlannedStartTime, result.PlannedStartTime);
        Assert.Equal(maintenance.PlannedEndDate, result.PlannedEndDate);
        Assert.Equal(maintenance.PlannedEndTime, result.PlannedEndTime);
        Assert.Equal("Пост 1", result.Location);
        Assert.Equal(315, result.Mileage);
        Assert.Equal(VehicleMileageUnit.EngineHours, result.MileageUnit);
        Assert.Equal("Комментарий", result.Comment);
        Assert.Equal(70m, result.Price);
        Assert.Equal($"#maintenance/{maintenance.Id}", result.DetailUrl);
        maintenance.Details = null!;
        Assert.Equal(0m, OrganizationWorkspaceMaintenanceMapper.Map(maintenance).Price);
    }

    [Theory]
    [InlineData(200, 3300)]
    [InlineData(4000, 0)]
    public void PriceShowsNetLineTotalAfterDiscountAndNeverGoesBelowZero(decimal discountAmount, decimal expectedPrice)
    {
        var maintenance = new MaintenanceEntity
        {
            Vehicle = new Vehicle(),
            DiscountAmount = discountAmount,
            Details =
            [
                new() { Price = 3000m, Quantity = 2 },
                new() { Price = 500m, Quantity = 3 }
            ]
        };

        var result = OrganizationWorkspaceMaintenanceMapper.Map(maintenance);

        Assert.Equal(expectedPrice, result.Price);
    }

    [Fact]
    public void CalendarUsesCatalogLogoAndPreservesServiceOrderWithoutMaterials()
    {
        var maintenance = new MaintenanceEntity
        {
            Vehicle = new Vehicle { BrandId = 1, Brand = new Brand { Name = "BMW", LogoKey = "bmw" }, RegistrationPlate = "А001АА" },
            Details =
            [
                new() { Position = 2, Name = "Керамика", EmployeeName = "Алексей" },
                new() { Position = 0, Name = "Мойка", EmployeeName = "Алексей" },
                new() { Position = 1, Name = "Состав", MaterialName = "Шампунь", EmployeeName = "Материалы" },
                new() { Position = 3, Name = "Полировка", EmployeeName = "Мария" }
            ]
        };

        var details = maintenance.Details;
        details[0].Position = 2;
        details[1].Position = 0;
        details[2].Position = 1;

        var result = OrganizationWorkspaceMaintenanceMapper.Map(maintenance);

        Assert.Equal("bmw", result.BrandLogoKey);
        Assert.Equal("А001АА", result.RegistrationPlate);
        Assert.Equal(new[] { "Мойка", "Керамика", "Полировка" }, result.ServiceNames);
        Assert.Equal(new[] { "Алексей", "Мария" }, result.EmployeeNames);
        maintenance.Vehicle.BrandId = null;
        Assert.Null(OrganizationWorkspaceMaintenanceMapper.Map(maintenance).BrandLogoKey);
    }

    [Fact]
    public void CalendarFallsBackToOrderPerformerForHistoricalServiceRows()
    {
        var maintenance = new MaintenanceEntity
        {
            Vehicle = new Vehicle(),
            Performer = "Сергей",
            Details = [new() { Name = "Мойка" }, new() { Name = "Полировка", EmployeeName = "Мария" }]
        };

        var result = OrganizationWorkspaceMaintenanceMapper.Map(maintenance);

        Assert.Equal(new[] { "Сергей", "Мария" }, result.EmployeeNames);
    }
}
