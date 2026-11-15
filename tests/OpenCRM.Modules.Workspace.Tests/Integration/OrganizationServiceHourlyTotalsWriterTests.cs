using System.Globalization;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Workspace.Queries;
using OpenCRM.Testing;
using Npgsql;
using MaintenanceEntity = OpenCRM.Domain.Maintenance;

namespace OpenCRM.Modules.Workspace.Tests.Integration;

public sealed class OrganizationServiceHourlyTotalsWriterTests(NotificationFixture fixture) : IClassFixture<NotificationFixture>
{
    private static readonly DateTime s_date = new(2026, 10, 19, 10, 15, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ChangesToDetailsDatesVisibilityAndOrganizationsPreserveSourceParity()
    {
        await using var context = this.CreateContext();
        var (organization, vehicle) = OrganizationServiceHourlyTotalsWriterTests.CreateVehicle();
        var recipient = new Organization { Name = "Получатель услуг", OwnerUser = vehicle.User };
        var first = OrganizationServiceHourlyTotalsWriterTests.CreateOrder(vehicle, s_date);
        var second = OrganizationServiceHourlyTotalsWriterTests.CreateOrder(vehicle, s_date.AddMinutes(5));
        var longName = new string(Enumerable.Range(0, 1000).Select(index => (char)(0x4E00 + index)).ToArray());
        first.Details.AddRange([
            new() { Name = longName, Price = 12.34m, Position = 1 },
            new() { Name = "полировка", Price = 3m, Position = 2 },
            new() { Name = " Полировка ", Price = 5m, Position = 3 },
            new() { Name = " ", Price = 2m, Position = 4 },
            new() { Name = "Без цены", Price = null, Position = 5 },
            new() { Name = "Отрицательная", Price = -2m, Position = 6 }
        ]);
        context.Organizations.Add(recipient);
        context.Maintenance.AddRange(first, second);
        await context.SaveChangesAsync();
        await OrganizationServiceHourlyTotalsWriterTests.AssertParityAsync(context, organization.Id);
        var parentVersion = await context.Database.SqlQuery<string>($"""
            SELECT xmin::text AS "Value" FROM "Maintenance" WHERE "Id" = {first.Id}
            """).SingleAsync();
        var renamed = first.Details[0];
        var moved = first.Details[1];
        var zeroed = first.Details[2];
        renamed.Name = "Новое имя";
        renamed.Price = 33m;
        renamed.Position = 8;
        context.MaintenanceDetails.Remove(moved);
        context.MaintenanceDetails.Add(new MaintenanceDetail
        {
            MaintenanceId = second.Id, DetailId = moved.DetailId, Name = moved.Name, Price = moved.Price, Position = 1
        });
        zeroed.Price = 0;
        await context.SaveChangesAsync();
        Assert.Equal(parentVersion, await context.Database.SqlQuery<string>($"""
            SELECT xmin::text AS "Value" FROM "Maintenance" WHERE "Id" = {first.Id}
            """).SingleAsync());
        await OrganizationServiceHourlyTotalsWriterTests.AssertParityAsync(context, organization.Id);
        second.CompletedDate = s_date.AddHours(2);
        first.CompletedDate = null;
        first.PlannedDate = s_date.AddHours(-2);
        await context.SaveChangesAsync();
        await OrganizationServiceHourlyTotalsWriterTests.AssertParityAsync(context, organization.Id);
        first.IsDeleted = true;
        await context.SaveChangesAsync();
        await OrganizationServiceHourlyTotalsWriterTests.AssertParityAsync(context, organization.Id);
        first.IsDeleted = false;
        first.Status = OpenCRM.Domain.Enums.MaintenanceStatus.Cancelled;
        await context.SaveChangesAsync();
        await OrganizationServiceHourlyTotalsWriterTests.AssertParityAsync(context, organization.Id);
        first.Status = OpenCRM.Domain.Enums.MaintenanceStatus.Completed;
        vehicle.IsDeleted = true;
        await context.SaveChangesAsync();
        await OrganizationServiceHourlyTotalsWriterTests.AssertParityAsync(context, organization.Id);
        vehicle.IsDeleted = false;
        vehicle.OrganizationId = recipient.Id;
        await context.SaveChangesAsync();
        await OrganizationServiceHourlyTotalsWriterTests.AssertParityAsync(context, organization.Id);
        await OrganizationServiceHourlyTotalsWriterTests.AssertParityAsync(context, recipient.Id);
        context.MaintenanceDetails.Remove(await context.MaintenanceDetails.SingleAsync(detail => detail.Name == longName
            && detail.MaintenanceId == second.Id));
        context.Maintenance.Remove(first);
        await context.SaveChangesAsync();
        await OrganizationServiceHourlyTotalsWriterTests.AssertParityAsync(context, recipient.Id);
        context.ChangeTracker.Clear();
        context.Organizations.Remove(await context.Organizations.SingleAsync(item => item.Id == recipient.Id));
        await context.SaveChangesAsync();
        Assert.False(await context.Set<OrganizationServiceHourlyTotal>().AnyAsync(row => row.OrganizationId == recipient.Id));
    }

    [Fact]
    public async Task RemovingAndRestoringFirstContributionRecomputesMinimumIncludingInfinityHours()
    {
        await using var context = this.CreateContext();
        var (organization, vehicle) = OrganizationServiceHourlyTotalsWriterTests.CreateVehicle();
        var earlier = OrganizationServiceHourlyTotalsWriterTests.CreateOrder(vehicle, s_date);
        var later = OrganizationServiceHourlyTotalsWriterTests.CreateOrder(vehicle, s_date.AddMinutes(10));
        var minimum = OrganizationServiceHourlyTotalsWriterTests.CreateOrder(vehicle, DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc));
        var maximum = OrganizationServiceHourlyTotalsWriterTests.CreateOrder(vehicle, DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc));
        later.Details.Add(new MaintenanceDetail { Name = "Полировка", Price = 7m, Position = 1 });
        minimum.Details.Add(new MaintenanceDetail { Name = "Полировка", Price = 3m, Position = 1 });
        maximum.Details.Add(new MaintenanceDetail { Name = "Полировка", Price = 4m, Position = 1 });
        var laterFirstDetail = later.Details[0];
        context.Maintenance.AddRange(earlier, later, minimum, maximum);
        await context.SaveChangesAsync();
        await OrganizationServiceHourlyTotalsWriterTests.AssertParityAsync(context, organization.Id);
        laterFirstDetail.Position = 5;
        minimum.Details[0].Price = 0;
        maximum.Details[0].Price = 0;
        await context.SaveChangesAsync();
        await OrganizationServiceHourlyTotalsWriterTests.AssertParityAsync(context, organization.Id);
        later.IsDeleted = true;
        await context.SaveChangesAsync();
        await OrganizationServiceHourlyTotalsWriterTests.AssertParityAsync(context, organization.Id);
        later.IsDeleted = false;
        laterFirstDetail.Position = 0;
        minimum.Details[0].Price = 10;
        maximum.Details[0].Price = 10;
        await context.SaveChangesAsync();
        await OrganizationServiceHourlyTotalsWriterTests.AssertParityAsync(context, organization.Id);
    }

    [Fact]
    public async Task ConcurrentFirstInsertsAndPriceUpdatesOfDifferentParentsShareExactlyOneGroup()
    {
        await using var seed = this.CreateContext();
        var (organization, vehicle) = OrganizationServiceHourlyTotalsWriterTests.CreateVehicle();
        seed.Vehicles.Add(vehicle);
        await seed.SaveChangesAsync();
        await using var firstContext = this.CreateContext();
        await using var secondContext = this.CreateContext();
        var first = new MaintenanceEntity
        {
            UserId = vehicle.UserId, VehicleId = vehicle.Id, Date = s_date,
            Details = [new() { Name = "Общая услуга", Price = 13m }]
        };
        var second = new MaintenanceEntity
        {
            UserId = vehicle.UserId, VehicleId = vehicle.Id, Date = s_date.AddMinutes(1),
            Details = [new() { Name = "Общая услуга", Price = 17m }]
        };
        firstContext.Maintenance.Add(first);
        secondContext.Maintenance.Add(second);
        await Task.WhenAll(firstContext.SaveChangesAsync(), secondContext.SaveChangesAsync()).WaitAsync(TimeSpan.FromSeconds(20));
        var group = await seed.Set<OrganizationServiceHourlyTotal>().AsNoTracking()
            .SingleAsync(row => row.OrganizationId == organization.Id);
        Assert.Equal(2, group.Count);
        Assert.Equal(30m, group.Amount);
        first.Details[0].Price = 29m;
        second.Details[0].Price = 41m;
        await Task.WhenAll(firstContext.SaveChangesAsync(), secondContext.SaveChangesAsync()).WaitAsync(TimeSpan.FromSeconds(20));
        await OrganizationServiceHourlyTotalsWriterTests.AssertParityAsync(seed, organization.Id);
        group = await seed.Set<OrganizationServiceHourlyTotal>().AsNoTracking()
            .SingleAsync(row => row.OrganizationId == organization.Id);
        Assert.Equal(2, group.Count);
        Assert.Equal(70m, group.Amount);
    }

    [Fact]
    public async Task RepeatableReadFirstInsertWithStaleSnapshotFailsWithoutDuplicatingGroup()
    {
        await using var seed = this.CreateContext();
        var (organization, vehicle) = OrganizationServiceHourlyTotalsWriterTests.CreateVehicle();
        seed.Vehicles.Add(vehicle);
        await seed.SaveChangesAsync();
        await using var stale = this.CreateContext();
        await using var transaction = await stale.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        Assert.False(await stale.Set<OrganizationServiceHourlyTotal>().AnyAsync(row => row.OrganizationId == organization.Id));
        await using (var concurrent = this.CreateContext())
        {
            concurrent.Maintenance.Add(new MaintenanceEntity
            {
                UserId = vehicle.UserId, VehicleId = vehicle.Id, Date = s_date,
                Details = [new() { Name = "Общая услуга", Price = 17m }]
            });
            await concurrent.SaveChangesAsync();
        }

        stale.Maintenance.Add(new MaintenanceEntity
        {
            UserId = vehicle.UserId, VehicleId = vehicle.Id, Date = s_date.AddMinutes(1),
            Details = [new() { Name = "Общая услуга", Price = 13m }]
        });
        var failure = await Assert.ThrowsAsync<PostgresException>(() => stale.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.SerializationFailure, failure.SqlState);
        stale.ChangeTracker.Clear();
        await transaction.CommitAsync();
        await OrganizationServiceHourlyTotalsWriterTests.AssertParityAsync(seed, organization.Id);
        var group = await seed.Set<OrganizationServiceHourlyTotal>().AsNoTracking()
            .SingleAsync(row => row.OrganizationId == organization.Id);
        Assert.Equal(1, group.Count);
        Assert.Equal(17m, group.Amount);
    }

    [Fact]
    public async Task SaveModesHonorAcceptFlagAndProjectionFailureRollsBackOwnAndExternalTransactions()
    {
        await using var context = this.CreateContext();
        var (organization, vehicle) = OrganizationServiceHourlyTotalsWriterTests.CreateVehicle();
        var order = OrganizationServiceHourlyTotalsWriterTests.CreateOrder(vehicle, s_date);
        context.Maintenance.Add(order);
        context.SaveChanges(false);
        Assert.Equal(EntityState.Added, context.Entry(order).State);
        context.ChangeTracker.AcceptAllChanges();
        order.Details[0].Price = 13m;
        await context.SaveChangesAsync(false);
        Assert.Equal(EntityState.Modified, context.Entry(order.Details[0]).State);
        context.ChangeTracker.AcceptAllChanges();
        var version = await context.Database.SqlQuery<string>($"""
            SELECT xmin::text AS "Value" FROM "OrganizationServiceHourlyTotals" WHERE "OrganizationId" = {organization.Id}
            """).SingleAsync();
        order.PaidAmount = 1m;
        await context.SaveChangesAsync();
        Assert.Equal(version, await context.Database.SqlQuery<string>($"""
            SELECT xmin::text AS "Value" FROM "OrganizationServiceHourlyTotals" WHERE "OrganizationId" = {organization.Id}
            """).SingleAsync());
        await context.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "OrganizationServiceHourlyTotals" ADD CONSTRAINT "CK_Test_ServiceAmountLimit" CHECK ("Amount" < 1000)
            """);
        try
        {
            order.Details[0].Price = 1001m;
            var ownFailure = Assert.Throws<PostgresException>(() => context.SaveChanges());
            Assert.Equal(PostgresErrorCodes.CheckViolation, ownFailure.SqlState);
            Assert.Equal(13m, await context.MaintenanceDetails.Where(detail => detail.MaintenanceId == order.Id)
                .Select(detail => detail.Price).SingleAsync());
            await using (var transaction = await context.Database.BeginTransactionAsync())
            {
                var externalFailure = await Assert.ThrowsAsync<PostgresException>(() => context.SaveChangesAsync());
                Assert.Equal(PostgresErrorCodes.CheckViolation, externalFailure.SqlState);
                context.ChangeTracker.Clear();
                await transaction.CommitAsync();
            }

            await OrganizationServiceHourlyTotalsWriterTests.AssertParityAsync(context, organization.Id);
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("""
                ALTER TABLE "OrganizationServiceHourlyTotals" DROP CONSTRAINT "CK_Test_ServiceAmountLimit"
                """);
        }
    }

    [Fact]
    public async Task CollisionWithExistingFullNameFailsAndRollsBackSource()
    {
        await using var context = this.CreateContext();
        var (organization, vehicle) = OrganizationServiceHourlyTotalsWriterTests.CreateVehicle();
        var order = OrganizationServiceHourlyTotalsWriterTests.CreateOrder(vehicle, s_date);
        context.Maintenance.Add(order);
        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "OrganizationServiceHourlyTotals" DROP CONSTRAINT "CK_OrganizationServiceHourlyTotals_NameHash"
            """);
        try
        {
            await context.Database.ExecuteSqlAsync($"""
                UPDATE "OrganizationServiceHourlyTotals" SET "Name" = 'Другое полное имя' WHERE "OrganizationId" = {organization.Id}
                """);
            order.Details[0].Price = 15m;
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            Assert.Equal(10m, await context.MaintenanceDetails.Where(detail => detail.MaintenanceId == order.Id)
                .Select(detail => detail.Price).SingleAsync());
        }
        finally
        {
            await context.Database.ExecuteSqlAsync($"""
                UPDATE "OrganizationServiceHourlyTotals" SET "Name" = 'Полировка' WHERE "OrganizationId" = {organization.Id}
                """);
            await context.Database.ExecuteSqlRawAsync("""
                ALTER TABLE "OrganizationServiceHourlyTotals" ADD CONSTRAINT "CK_OrganizationServiceHourlyTotals_NameHash"
                CHECK ("NameHash" = encode(sha256(convert_to("Name", 'UTF8')), 'hex'))
                """);
        }
    }

    [Fact]
    public async Task PhysicalUserAndVehicleCascadesRemoveContributionsFromSurvivingOrganization()
    {
        await using var context = this.CreateContext();
        var (organization, vehicle) = OrganizationServiceHourlyTotalsWriterTests.CreateVehicle();
        organization.OwnerUser = new ApplicationUser();
        var secondVehicle = new Vehicle
        {
            User = organization.OwnerUser, Organization = organization, ManualBrandName = "BMW", ManualModelName = "X3"
        };
        context.Maintenance.AddRange(OrganizationServiceHourlyTotalsWriterTests.CreateOrder(vehicle, s_date),
            OrganizationServiceHourlyTotalsWriterTests.CreateOrder(secondVehicle, s_date));
        await context.SaveChangesAsync();
        var deletedUserId = vehicle.User.Id;
        context.ChangeTracker.Clear();
        context.Users.Remove(await context.Users.SingleAsync(user => user.Id == deletedUserId));
        await context.SaveChangesAsync();
        await OrganizationServiceHourlyTotalsWriterTests.AssertParityAsync(context, organization.Id);
        context.Vehicles.Remove(await context.Vehicles.SingleAsync(item => item.Id == secondVehicle.Id));
        await context.SaveChangesAsync();
        await OrganizationServiceHourlyTotalsWriterTests.AssertParityAsync(context, organization.Id);
    }

    private OpenCRMContext CreateContext()
    {
        using var source = fixture.CreateContext();
        var options = new DbContextOptionsBuilder<OpenCRMContext>().UseNpgsql(source.Database.GetConnectionString()).Options;

        return new OpenCRMContext(options);
    }

    private static (Organization Organization, Vehicle Vehicle) CreateVehicle()
    {
        var user = new ApplicationUser();
        var organization = new Organization { Name = "Почасовые суммы услуг", OwnerUser = user };
        var vehicle = new Vehicle { User = user, Organization = organization, ManualBrandName = "BMW", ManualModelName = "X3" };

        return (organization, vehicle);
    }

    private static MaintenanceEntity CreateOrder(Vehicle vehicle, DateTime date) => new()
    {
        User = vehicle.User, Vehicle = vehicle, Date = date, CompletedDate = date, DateTimeCreation = s_date,
        Details = [new() { Name = "Полировка", Price = 10m }]
    };

    private static async Task AssertParityAsync(OpenCRMContext context, Guid organizationId)
    {
        var orders = await context.Maintenance.AsNoTracking().IgnoreQueryFilters()
            .Where(order => order.Vehicle.OrganizationId == organizationId && !order.IsDeleted && !order.Vehicle.IsDeleted)
            .Include(order => order.Details).ToListAsync();
        var expected = orders.SelectMany(order => order.Details
                .Where(detail => order.Status != OpenCRM.Domain.Enums.MaintenanceStatus.Cancelled
                    && detail.Price > 0 && detail.Name != null)
                .Select(detail => new
                {
                    Hour = (int)((order.CompletedDate ?? order.PlannedDate ?? order.Date).Ticks / TimeSpan.TicksPerHour),
                    detail.Name,
                    Amount = detail.Price!.Value,
                    FirstKey = (315537897599999999L - (order.CompletedDate ?? order.PlannedDate ?? order.Date).Ticks / 10)
                        .ToString("D18", CultureInfo.InvariantCulture) + ":" + order.Id.ToString("D") + ":"
                        + detail.Position.ToString("D10", CultureInfo.InvariantCulture) + ":" + detail.DetailId.ToString("D")
                }))
            .GroupBy(detail => (detail.Hour, detail.Name)).ToDictionary(group => group.Key, group => new
            {
                Count = group.LongCount(), Amount = group.Sum(detail => detail.Amount),
                FirstKey = group.Select(detail => detail.FirstKey).Order(StringComparer.Ordinal).First()
            });
        var actual = await context.Set<OrganizationServiceHourlyTotal>().AsNoTracking()
            .Where(row => row.OrganizationId == organizationId).ToListAsync();
        Assert.Equal(expected.Count, actual.Count);
        foreach (var row in actual)
        {
            Assert.True(expected.TryGetValue((row.UtcHour, row.Name), out var source));
            Assert.Equal(source.Count, row.Count);
            Assert.Equal(source.Amount, row.Amount);
            Assert.Equal(source.FirstKey, row.FirstKey);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(row.Name))), row.NameHash);
        }

        if (orders.All(order => (order.CompletedDate ?? order.PlannedDate ?? order.Date).Year == s_date.Year))
        {
            var expectedFinance = await new FinanceQueryOracle(context).GetFinanceSummaryAsync(organizationId, s_date.Date, s_date.Date);
            var actualFinance = await new OrganizationFinanceQueryService(context).GetFinanceSummaryAsync(organizationId, s_date.Date, s_date.Date);
            Assert.True(JsonNode.DeepEquals(JsonSerializer.SerializeToNode(expectedFinance.TopServices),
                JsonSerializer.SerializeToNode(actualFinance.TopServices)));
        }
    }
}
