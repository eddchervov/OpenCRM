using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Workspace.Personal;
using OpenCRM.Testing;
using MaintenanceEntity = OpenCRM.Domain.Maintenance;

namespace OpenCRM.Modules.Workspace.Tests.Integration;

public sealed class HomeAnalyticsTests(NotificationFixture fixture) : IClassFixture<NotificationFixture>
{
    [Fact]
    public async Task PersonalAnalyticsLoadsDetailsAndExcludesForeignDeletedAndArchivedData()
    {
        await using var context = fixture.CreateContext();
        var owner = new ApplicationUser();
        var foreignUser = new ApplicationUser();
        var vehicle = new Vehicle { User = owner, ManualBrandName = "Марка", ManualModelName = "Модель" };
        var archivedVehicle = new Vehicle { User = owner, IsArchived = true };
        var foreignVehicle = new Vehicle { User = foreignUser };
        context.Maintenance.AddRange(
            new MaintenanceEntity
            {
                User = owner, Vehicle = vehicle, Date = DateTime.UtcNow,
                Details = [new MaintenanceDetail { DetailId = Guid.NewGuid(), Name = "Работа", Price = 250 }]
            },
            new MaintenanceEntity { User = owner, Vehicle = vehicle, Date = DateTime.UtcNow },
            new MaintenanceEntity
            {
                User = owner, Vehicle = vehicle, IsDeleted = true, Date = DateTime.UtcNow,
                Details = [new MaintenanceDetail { DetailId = Guid.NewGuid(), Name = "Удалённая работа", Price = 900 }]
            },
            new MaintenanceEntity
            {
                User = owner, Vehicle = archivedVehicle, Date = DateTime.UtcNow,
                Details = [new MaintenanceDetail { DetailId = Guid.NewGuid(), Name = "Архивная работа", Price = 800 }]
            },
            new MaintenanceEntity
            {
                User = foreignUser, Vehicle = foreignVehicle, Date = DateTime.UtcNow,
                Details = [new MaintenanceDetail { DetailId = Guid.NewGuid(), Name = "Чужая работа", Price = 700 }]
            });
        await context.SaveChangesAsync();
        await using var readContext = fixture.CreateContext();

        var result = await new HomeAnalyticsService(readContext).GetAsync(owner.Id, 1);

        Assert.Equal(1, result.VehicleCount);
        Assert.Equal(2, result.MaintenanceCount);
        Assert.Equal(250, result.TotalSpent);
        Assert.Equal(250, result.AverageMaintenanceCost);
        Assert.Equal(250, result.TopVehicleSpent);
        Assert.Equal(250, Assert.Single(result.MonthlyCosts).Total);
        Assert.Contains("Марка", result.TopVehicleName);
    }
}
