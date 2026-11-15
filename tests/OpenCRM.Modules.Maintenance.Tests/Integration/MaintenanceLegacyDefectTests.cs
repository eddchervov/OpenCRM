using Microsoft.EntityFrameworkCore;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Infrastructure.Data.Repositories;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Testing;

namespace OpenCRM.Modules.Maintenance.Tests.Integration;

public sealed class MaintenanceLegacyDefectTests(MaintenanceDatabaseFixture fixture) : IClassFixture<MaintenanceDatabaseFixture>
{
    [Fact]
    public async Task PersonalCompletionClosesRetainedHistoricalDefectLink()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var historicalOrganization = new Organization { OwnerUser = user, Name = "Прежнее рабочее пространство" };
        var date = new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc);
        var vehicle = new Vehicle { User = user };
        var work = new MaintenanceEntity
        {
            User = user,
            Vehicle = vehicle,
            Date = date,
            PlannedDate = date,
            Status = MaintenanceStatus.Planned,
            Details = [new MaintenanceDetail { Name = "Историческая работа", Price = 100 }]
        };
        var defect = new VehicleDefect
        {
            User = user,
            Vehicle = vehicle,
            Organization = historicalOrganization,
            Title = "Сохранённая историческая неисправность"
        };
        work.LinkedDefects.Add(new MaintenanceDefect
        {
            User = user,
            Organization = historicalOrganization,
            VehicleDefect = defect
        });
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        using var scope = new MaintenanceTestScope(
            new MaintenanceRepository(context),
            Mock.Of<IVehicleCounterCommandService>(), Mock.Of<ISystemClock>(value => value.UtcNow == date),
            context, Mock.Of<IOrganizationSubscriptionService>(),
            Mock.Of<IMaintenanceQueryService>(), Mock.Of<IMaintenanceSlotQueryService>(), null!);

        await scope.Get<IMaintenanceUpdateService>().Update(user.Id, work.Id, new UpdateMaintenanceDto
        {
            Version = work.Version,
            Type = work.Type,
            Status = MaintenanceStatus.Completed,
            PlannedDate = date,
            CompletedDate = date,
            Location = null,
            Performer = null,
            Mileage = null,
            Comment = null,
            Details = [new CreateMaintenanceDetailDto { DetailId = work.Details.Single().DetailId, Name = "Историческая работа", Price = 100 }]
        });

        Assert.Equal(VehicleDefectStatusCodes.Resolved, (await context.VehicleDefects.SingleAsync(value => value.Id == defect.Id)).StatusCode);
        Assert.True(await context.MaintenanceDefects.AnyAsync(value => value.MaintenanceId == work.Id && value.VehicleDefectId == defect.Id));
        Assert.Equal(MaintenanceStatus.Completed, (await context.Maintenance.AsNoTracking().SingleAsync(value => value.Id == work.Id)).Status);
    }
}
