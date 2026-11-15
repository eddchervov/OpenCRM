using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Workspace.Queries;
using OpenCRM.Testing;
using MaintenanceEntity = OpenCRM.Domain.Maintenance;

namespace OpenCRM.Modules.Workspace.Tests.Integration;

public sealed class OrganizationAnalyticsAggregationTests(NotificationFixture fixture) : IClassFixture<NotificationFixture>
{
    [Fact]
    public async Task DailyAggregationMatchesOriginalForPricePrecisionDatesAndOrganizationScope()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var organization = new Organization { Name = "Проверка статистики", OwnerUser = user };
        var foreignOrganization = new Organization { Name = "Чужая организация", OwnerUser = user };
        var vehicle = new Vehicle { User = user, Organization = organization };
        var archivedVehicle = new Vehicle { User = user, Organization = organization, IsArchived = true };
        var deletedVehicle = new Vehicle { User = user, Organization = organization, IsDeleted = true };
        var foreignVehicle = new Vehicle { User = user, Organization = foreignOrganization };
        var today = new DateTime(2026, 10, 19, 0, 0, 0, DateTimeKind.Utc);
        decimal?[] prices = [1.005m, -1.005m, 0.10000000000000002m, 123456789.12345678m, null, 0.0000000000000001m];
        for (var index = 0; index < 180; index++)
        {
            var date = today.AddDays(-(index % 16)).AddHours(index % 24);
            context.Maintenance.Add(new MaintenanceEntity
            {
                User = user,
                Vehicle = index % 19 == 0 ? deletedVehicle : index % 17 == 0 ? foreignVehicle :
                    index % 13 == 0 ? archivedVehicle : vehicle,
                Date = date,
                CompletedDate = index % 2 == 0 ? date : null,
                PlannedDate = today.AddDays(10),
                DateTimeCreation = date.AddHours(-2),
                Status = index % 11 == 0 ? MaintenanceStatus.Planned :
                    index % 7 == 0 ? MaintenanceStatus.InProgress : MaintenanceStatus.Completed,
                IsDeleted = index % 23 == 0,
                PaidAmount = index % 3 == 0 ? 0m : index % 3 == 1 ? 12.34m : -1m,
                DiscountAmount = index % 4 == 0 ? 200000000m : 0.01m,
                Details = index % 5 == 0 ? [] : prices.Select((price, position) => new MaintenanceDetail
                {
                    Name = "Точная цена",
                    Price = price,
                    Position = position,
                    Quantity = 3
                }).ToList()
            });
        }

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        await context.Database.OpenConnectionAsync();
        try
        {
            await context.Database.ExecuteSqlRawAsync("SET TIME ZONE 'Asia/Novosibirsk'");
            var expected = await new AnalyticsQueryOracle(context).GetAnalyticsAsync(
                organization.Id, true, false, false, true, today);
            var actual = await new OrganizationWorkspaceAnalyticsQueryService(context).GetAnalyticsAsync(
                organization.Id, true, false, false, true, today);

            Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
            Assert.Empty(context.ChangeTracker.Entries());

            var empty = await new OrganizationWorkspaceAnalyticsQueryService(context).GetAnalyticsAsync(
                Guid.NewGuid(), false, false, false, false, today);
            Assert.Equal(0m, empty.Kpis.Revenue14Days);
            Assert.All(empty.RevenueSeries, point => Assert.Equal(0m, point.Value));
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("SET TIME ZONE 'UTC'");
            await context.Database.CloseConnectionAsync();
        }
    }
}
