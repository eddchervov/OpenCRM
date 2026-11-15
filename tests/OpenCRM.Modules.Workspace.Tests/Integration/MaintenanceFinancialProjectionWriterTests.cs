using System.Text.Json;
using System.Text.Json.Nodes;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Workspace.Queries;
using OpenCRM.Testing;
using Npgsql;
using MaintenanceEntity = OpenCRM.Domain.Maintenance;

namespace OpenCRM.Modules.Workspace.Tests.Integration;

public sealed class MaintenanceFinancialProjectionWriterTests(NotificationFixture fixture) : IClassFixture<NotificationFixture>
{
    private static readonly DateTime s_testDate = new(2026, 10, 19, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task CancellationKeepsSourceAmountsAndPaymentsButRemovesAccrualAndServiceContributions()
    {
        await using var context = this.CreateContext();
        var (organization, vehicle) = MaintenanceFinancialProjectionWriterTests.CreateVehicle();
        var order = MaintenanceFinancialProjectionWriterTests.CreateOrder(vehicle, s_testDate);
        order.Details[0].Price = 3000m;
        order.DiscountAmount = 200m;
        order.PaidAmount = 500m;
        order.MaterialCostAmount = 120m;
        order.LaborCostAmount = 80m;
        context.Maintenance.Add(order);
        await context.SaveChangesAsync();

        order.Status = OpenCRM.Domain.Enums.MaintenanceStatus.Cancelled;
        await context.SaveChangesAsync();
        var cancelled = await context.MaintenanceFinancialTotals.AsNoTracking().SingleAsync(row => row.MaintenanceId == order.Id);

        Assert.True(cancelled.IsVisible);
        Assert.False(cancelled.IsAccruable);
        Assert.Equal(3000m, cancelled.GrossAmount);
        Assert.Equal(200m, cancelled.DiscountAmount);
        Assert.Equal(0m, cancelled.NetAmount);
        Assert.Equal(500m, cancelled.PaidAmount);
        Assert.Equal(0m, cancelled.DebtAmount);
        Assert.Equal(120m, cancelled.MaterialCostAmount);
        Assert.Equal(80m, cancelled.LaborCostAmount);
        Assert.Equal(-200m, cancelled.ProfitAmount);
        Assert.Empty(await context.Set<OrganizationServiceHourlyTotal>().Where(row => row.OrganizationId == organization.Id).ToListAsync());
        await MaintenanceFinancialProjectionWriterTests.AssertParityAsync(context, organization.Id);

        order.Status = OpenCRM.Domain.Enums.MaintenanceStatus.Completed;
        await context.SaveChangesAsync();
        var restored = await context.MaintenanceFinancialTotals.AsNoTracking().SingleAsync(row => row.MaintenanceId == order.Id);

        Assert.True(restored.IsAccruable);
        Assert.Equal(2800m, restored.NetAmount);
        Assert.Equal(2300m, restored.DebtAmount);
        await MaintenanceFinancialProjectionWriterTests.AssertParityAsync(context, organization.Id);
    }

    [Fact]
    public async Task DetailAndOrderChangesPreserveSourceParityAndParentVersion()
    {
        await using var context = this.CreateContext();
        var (organization, vehicle) = MaintenanceFinancialProjectionWriterTests.CreateVehicle();
        var buyer = new OrganizationClient { Organization = organization, Name = "Покупатель" };
        var older = MaintenanceFinancialProjectionWriterTests.CreateOrder(vehicle, s_testDate);
        var latest = MaintenanceFinancialProjectionWriterTests.CreateOrder(vehicle, s_testDate.AddMinutes(5));
        older.BuyerClientId = buyer.Id;
        older.BuyerName = " Покупатель ";
        latest.BuyerClientId = buyer.Id;
        latest.BuyerName = " Покупатель ";
        context.OrganizationClients.Add(buyer);
        context.Maintenance.AddRange(older, latest);
        await context.SaveChangesAsync();
        await MaintenanceFinancialProjectionWriterTests.AssertParityAsync(context, organization.Id);
        var parentVersion = await MaintenanceFinancialProjectionWriterTests.VersionAsync(context, latest.Id);
        var longName = new string(Enumerable.Range(0, 1000).Select(index => (char)(0x4E00 + index)).ToArray());
        latest.Details[0].Name = longName;
        latest.Details[0].Price = 12.34m;
        latest.Details[0].Position = 9;
        context.MaintenanceDetails.Add(new MaintenanceDetail
        {
            MaintenanceId = latest.Id, Name = " ", Price = 1m, Position = 10
        });
        await context.SaveChangesAsync();
        Assert.Equal(parentVersion, await MaintenanceFinancialProjectionWriterTests.VersionAsync(context, latest.Id));
        await MaintenanceFinancialProjectionWriterTests.AssertParityAsync(context, organization.Id);
        latest.Details[0].Price = null;
        older.Details[0].Price = -3m;
        await context.SaveChangesAsync();
        await MaintenanceFinancialProjectionWriterTests.AssertParityAsync(context, organization.Id);
        latest.DiscountAmount = -2m;
        latest.PaidAmount = -5m;
        latest.MaterialCostAmount = 3m;
        latest.LaborCostAmount = 4m;
        latest.BuyerName = "Новое имя";
        latest.CompletedDate = s_testDate.AddHours(2);
        await context.SaveChangesAsync();
        await MaintenanceFinancialProjectionWriterTests.AssertParityAsync(context, organization.Id);
        latest.IsDeleted = true;
        await context.SaveChangesAsync();
        await MaintenanceFinancialProjectionWriterTests.AssertParityAsync(context, organization.Id);
        latest.IsDeleted = false;
        latest.CompletedDate = null;
        latest.PlannedDate = s_testDate.AddHours(3);
        latest.BuyerClientId = null;
        await context.SaveChangesAsync();
        await MaintenanceFinancialProjectionWriterTests.AssertParityAsync(context, organization.Id);
        context.Maintenance.Remove(latest);
        await context.SaveChangesAsync();
        await MaintenanceFinancialProjectionWriterTests.AssertParityAsync(context, organization.Id);
    }

    [Fact]
    public async Task VehicleTransferDeletionRestoreAndPhysicalBuyerDeletionUpdateAllOrders()
    {
        await using var context = this.CreateContext();
        var (organization, vehicle) = MaintenanceFinancialProjectionWriterTests.CreateVehicle();
        var other = new Organization { Name = "Получатель техники", OwnerUser = vehicle.User };
        var buyer = new OrganizationClient { Organization = organization, Name = "Клиент" };
        var orders = Enumerable.Range(0, 35).Select(index =>
        {
            var order = MaintenanceFinancialProjectionWriterTests.CreateOrder(vehicle, s_testDate.AddMinutes(index));
            order.BuyerClientId = buyer.Id;
            order.BuyerName = index % 2 == 0 ? " Клиент " : "Клиент";

            return order;
        }).ToArray();
        context.Organizations.Add(other);
        context.OrganizationClients.Add(buyer);
        context.Maintenance.AddRange(orders);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var storedVehicle = await context.Vehicles.SingleAsync(item => item.Id == vehicle.Id);
        storedVehicle.OrganizationId = other.Id;
        await context.SaveChangesAsync();
        await MaintenanceFinancialProjectionWriterTests.AssertParityAsync(context, organization.Id);
        await MaintenanceFinancialProjectionWriterTests.AssertParityAsync(context, other.Id);
        storedVehicle.IsDeleted = true;
        await context.SaveChangesAsync();
        await MaintenanceFinancialProjectionWriterTests.AssertParityAsync(context, other.Id);
        storedVehicle.IsDeleted = false;
        await context.SaveChangesAsync();
        context.OrganizationClients.Remove(await context.OrganizationClients.SingleAsync(item => item.Id == buyer.Id));
        await context.SaveChangesAsync();
        Assert.All(await context.Maintenance.Where(order => order.VehicleId == vehicle.Id).ToListAsync(),
            order => Assert.Null(order.BuyerClientId));
        await MaintenanceFinancialProjectionWriterTests.AssertParityAsync(context, other.Id);
        var cascadeBuyer = new OrganizationClient { OrganizationId = organization.Id, Name = "Клиент удаляемой организации" };
        context.OrganizationClients.Add(cascadeBuyer);
        var transferredOrders = await context.Maintenance.Where(order => order.VehicleId == vehicle.Id).ToListAsync();
        foreach (var order in transferredOrders)
        {
            order.BuyerClientId = cascadeBuyer.Id;
            order.BuyerName = "Снимок клиента до удаления организации";
        }

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        context.Organizations.Remove(await context.Organizations.SingleAsync(item => item.Id == organization.Id));
        await context.SaveChangesAsync();
        Assert.False(await context.OrganizationClients.AnyAsync(item => item.Id == cascadeBuyer.Id));
        Assert.All(await context.Maintenance.Where(order => order.VehicleId == vehicle.Id).ToListAsync(),
            order => Assert.Null(order.BuyerClientId));
        await MaintenanceFinancialProjectionWriterTests.AssertParityAsync(context, other.Id);
        storedVehicle = await context.Vehicles.SingleAsync(item => item.Id == vehicle.Id);
        context.Vehicles.Remove(storedVehicle);
        await context.SaveChangesAsync();
        await MaintenanceFinancialProjectionWriterTests.AssertParityAsync(context, other.Id);
    }

    [Fact]
    public async Task PersonalDeletedAndInfinityDatedOrdersKeepCompleteProjection()
    {
        await using var context = this.CreateContext();
        var (organization, vehicle) = MaintenanceFinancialProjectionWriterTests.CreateVehicle();
        var minimum = MaintenanceFinancialProjectionWriterTests.CreateOrder(vehicle,
            DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc));
        var maximum = MaintenanceFinancialProjectionWriterTests.CreateOrder(vehicle,
            DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc));
        maximum.IsDeleted = true;
        context.Maintenance.AddRange(minimum, maximum);
        await context.SaveChangesAsync();
        await MaintenanceFinancialProjectionWriterTests.AssertParityAsync(context, organization.Id);
        context.Organizations.Remove(organization);
        await context.SaveChangesAsync();
        var projections = await context.MaintenanceFinancialTotals.AsNoTracking()
            .Where(row => row.MaintenanceId == minimum.Id || row.MaintenanceId == maximum.Id).ToListAsync();

        Assert.Equal(2, projections.Count);
        Assert.All(projections, row =>
        {
            Assert.Null(row.OrganizationId);
            Assert.False(row.IsVisible);
            Assert.Equal(10m, row.GrossAmount);
            Assert.Equal(10m, row.NetAmount);
        });
    }

    [Fact]
    public async Task PhysicalUserCascadeRemovesContributionsFromSurvivingOrganization()
    {
        await using var context = this.CreateContext();
        var (organization, vehicle) = MaintenanceFinancialProjectionWriterTests.CreateVehicle();
        organization.OwnerUser = new ApplicationUser();
        var userId = vehicle.User.Id;
        context.Maintenance.Add(MaintenanceFinancialProjectionWriterTests.CreateOrder(vehicle, s_testDate));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        context.Users.Remove(await context.Users.SingleAsync(user => user.Id == userId));
        await context.SaveChangesAsync();

        Assert.True(await context.Organizations.AnyAsync(item => item.Id == organization.Id));
        await MaintenanceFinancialProjectionWriterTests.AssertParityAsync(context, organization.Id);
    }

    [Fact]
    public async Task ConcurrentOrdersAndServiceRenamingRemainConsistent()
    {
        await using var firstContext = this.CreateContext();
        var (organization, vehicle) = MaintenanceFinancialProjectionWriterTests.CreateVehicle();
        var first = MaintenanceFinancialProjectionWriterTests.CreateOrder(vehicle, s_testDate);
        var second = MaintenanceFinancialProjectionWriterTests.CreateOrder(vehicle, s_testDate.AddMinutes(2));
        firstContext.Maintenance.AddRange(first, second);
        await firstContext.SaveChangesAsync();
        await using var secondContext = this.CreateContext();
        var concurrentDetail = await secondContext.MaintenanceDetails.SingleAsync(detail => detail.MaintenanceId == second.Id);
        first.Details[0].Price = 27m;
        concurrentDetail.Price = 43m;
        await Task.WhenAll(firstContext.SaveChangesAsync(), secondContext.SaveChangesAsync());
        await MaintenanceFinancialProjectionWriterTests.AssertParityAsync(firstContext, organization.Id);
        var projectionVersion = await firstContext.Database.SqlQuery<string>($"""
            SELECT xmin::text AS "Value" FROM "MaintenanceFinancialTotals" WHERE "MaintenanceId" = {second.Id}
            """).SingleAsync();
        concurrentDetail.Name = "Другая услуга";
        await secondContext.SaveChangesAsync();
        Assert.Equal(projectionVersion, await firstContext.Database.SqlQuery<string>($"""
            SELECT xmin::text AS "Value" FROM "MaintenanceFinancialTotals" WHERE "MaintenanceId" = {second.Id}
            """).SingleAsync());
        await MaintenanceFinancialProjectionWriterTests.AssertParityAsync(firstContext, organization.Id);
        concurrentDetail.Name = "Полировка";
        await secondContext.SaveChangesAsync();
        secondContext.MaintenanceDetails.Remove(concurrentDetail);
        await secondContext.SaveChangesAsync();
        await MaintenanceFinancialProjectionWriterTests.AssertParityAsync(firstContext, organization.Id);
    }

    [Fact]
    public async Task ProjectionFailureRollsBackSourceAndExternalSavepointAndBothSaveModesHonorAcceptFlag()
    {
        await using var context = this.CreateContext();
        var (organization, vehicle) = MaintenanceFinancialProjectionWriterTests.CreateVehicle();
        var order = MaintenanceFinancialProjectionWriterTests.CreateOrder(vehicle, s_testDate);
        context.Maintenance.Add(order);
        context.SaveChanges(false);
        Assert.Equal(EntityState.Added, context.Entry(order).State);
        context.ChangeTracker.AcceptAllChanges();
        var detail = order.Details[0];
        detail.Price = 13m;
        await context.SaveChangesAsync(false);
        Assert.Equal(EntityState.Modified, context.Entry(detail).State);
        context.ChangeTracker.AcceptAllChanges();
        await MaintenanceFinancialProjectionWriterTests.AssertParityAsync(context, organization.Id);
        await context.Database.ExecuteSqlRawAsync(
            """ALTER TABLE "MaintenanceFinancialTotals" ADD CONSTRAINT "CK_Test_ProjectionWriterLimit" CHECK ("GrossAmount" < 1000)""");
        try
        {
            detail.Price = 1001m;
            var ownFailure = await Assert.ThrowsAsync<PostgresException>(() => context.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, ownFailure.SqlState);
            Assert.Equal(13m, await context.MaintenanceDetails.Where(item => item.MaintenanceId == order.Id)
                .Select(item => item.Price).SingleAsync());
            await using (var transaction = await context.Database.BeginTransactionAsync())
            {
                var externalFailure = await Assert.ThrowsAsync<PostgresException>(() => context.SaveChangesAsync());
                Assert.Equal(PostgresErrorCodes.CheckViolation, externalFailure.SqlState);
                context.ChangeTracker.Clear();
                await transaction.CommitAsync();
            }

            await MaintenanceFinancialProjectionWriterTests.AssertParityAsync(context, organization.Id);
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE "MaintenanceFinancialTotals" DROP CONSTRAINT "CK_Test_ProjectionWriterLimit"
                """);
        }
    }

    [Fact]
    public async Task TransferIncludesOrderWhoseInsertCommittedWhileVehicleLockWasWaiting()
    {
        await using var seed = this.CreateContext();
        var (organization, vehicle) = MaintenanceFinancialProjectionWriterTests.CreateVehicle();
        var other = new Organization { Name = "Конкурентный перенос", OwnerUser = vehicle.User };
        seed.Organizations.Add(other);
        seed.Maintenance.Add(MaintenanceFinancialProjectionWriterTests.CreateOrder(vehicle, s_testDate));
        await seed.SaveChangesAsync();
        await using var inserting = this.CreateContext();
        await using var transaction = await inserting.Database.BeginTransactionAsync();
        var inserted = new MaintenanceEntity
        {
            VehicleId = vehicle.Id, UserId = vehicle.UserId, Date = s_testDate.AddMinutes(3), DateTimeCreation = s_testDate,
            Details = [new() { Name = "Вставленная услуга", Price = 37m }]
        };
        inserting.Maintenance.Add(inserted);
        await inserting.SaveChangesAsync();
        await using var transferring = this.CreateContext();
        var storedVehicle = await transferring.Vehicles.SingleAsync(item => item.Id == vehicle.Id);
        storedVehicle.OrganizationId = other.Id;
        var transfer = transferring.SaveChangesAsync();
        await Task.Delay(100);
        Assert.False(transfer.IsCompleted);
        await transaction.CommitAsync();
        await transfer.WaitAsync(TimeSpan.FromSeconds(15));
        await MaintenanceFinancialProjectionWriterTests.AssertParityAsync(seed, organization.Id);
        await MaintenanceFinancialProjectionWriterTests.AssertParityAsync(seed, other.Id);
    }

    [Fact]
    public async Task ExistingParentLockAndVehicleTransferCompleteWithoutLockInversion()
    {
        await using var seed = this.CreateContext();
        var (organization, vehicle) = MaintenanceFinancialProjectionWriterTests.CreateVehicle();
        var other = new Organization { Name = "Перенос после редактирования", OwnerUser = vehicle.User };
        var order = MaintenanceFinancialProjectionWriterTests.CreateOrder(vehicle, s_testDate);
        seed.Organizations.Add(other);
        seed.Maintenance.Add(order);
        await seed.SaveChangesAsync();
        await using var editing = this.CreateContext();
        await using var transaction = await editing.Database.BeginTransactionAsync();
        await editing.Database.ExecuteSqlAsync($"""SELECT "Id" FROM "Maintenance" WHERE "Id" = {order.Id} FOR UPDATE""");
        await using var transferring = this.CreateContext();
        var storedVehicle = await transferring.Vehicles.SingleAsync(item => item.Id == vehicle.Id);
        storedVehicle.OrganizationId = other.Id;
        var transfer = transferring.SaveChangesAsync();
        var detail = await editing.MaintenanceDetails.SingleAsync(item => item.MaintenanceId == order.Id);
        detail.Price = 29m;
        await editing.SaveChangesAsync().WaitAsync(TimeSpan.FromSeconds(15));
        await transaction.CommitAsync();
        await transfer.WaitAsync(TimeSpan.FromSeconds(15));
        await MaintenanceFinancialProjectionWriterTests.AssertParityAsync(seed, organization.Id);
        await MaintenanceFinancialProjectionWriterTests.AssertParityAsync(seed, other.Id);
    }

    private OpenCRMContext CreateContext()
    {
        using var source = fixture.CreateContext();
        var options = new DbContextOptionsBuilder<OpenCRMContext>()
            .UseNpgsql(source.Database.GetConnectionString()).Options;

        return new OpenCRMContext(options);
    }

    private static (Organization Organization, Vehicle Vehicle) CreateVehicle()
    {
        var user = new ApplicationUser();
        var organization = new Organization { Name = "Проверка финансовой проекции", OwnerUser = user };
        var vehicle = new Vehicle
        {
            User = user, Organization = organization, ManualBrandName = "BMW", ManualModelName = "X3"
        };

        return (organization, vehicle);
    }

    private static MaintenanceEntity CreateOrder(Vehicle vehicle, DateTime date) => new()
    {
        User = vehicle.User, Vehicle = vehicle, Date = date, CompletedDate = date, DateTimeCreation = date,
        Details = [new() { Name = "Полировка", Price = 10m }]
    };

    private static Task<string> VersionAsync(OpenCRMContext context, Guid id) =>
        context.Database.SqlQuery<string>($"""SELECT xmin::text AS "Value" FROM "Maintenance" WHERE "Id" = {id}""").SingleAsync();

    private static async Task AssertParityAsync(OpenCRMContext context, Guid organizationId)
    {
        var orders = await context.Maintenance.AsNoTracking().IgnoreQueryFilters()
            .Include(order => order.Vehicle).Include(order => order.Details)
            .Where(order => order.Vehicle.OrganizationId == organizationId).ToListAsync();
        var projections = await context.MaintenanceFinancialTotals.AsNoTracking()
            .Where(row => row.OrganizationId == organizationId).ToDictionaryAsync(row => row.MaintenanceId);
        Assert.Equal(orders.Count, projections.Count);
        foreach (var order in orders)
        {
            var row = projections[order.Id];
            var effectiveDate = order.CompletedDate ?? order.PlannedDate ?? order.Date;
            var gross = order.Details.Sum(detail => detail.Price ?? 0);
            var net = order.Status == OpenCRM.Domain.Enums.MaintenanceStatus.Cancelled
                ? 0 : Math.Max(0, gross - order.DiscountAmount);
            Assert.Equal(organizationId, row.OrganizationId);
            Assert.Equal(effectiveDate, row.EffectiveDate);
            Assert.Equal((int)(effectiveDate.Ticks / TimeSpan.TicksPerDay), row.UtcDay);
            Assert.Equal(!order.IsDeleted && !order.Vehicle.IsDeleted, row.IsVisible);
            Assert.Equal(!order.IsDeleted && !order.Vehicle.IsDeleted
                && order.Status != OpenCRM.Domain.Enums.MaintenanceStatus.Cancelled, row.IsAccruable);
            Assert.Equal(order.BuyerClientId, row.ClientId);
            Assert.Equal(order.BuyerClientId.HasValue ? order.BuyerName : null, row.ClientName);
            Assert.Equal(gross, row.GrossAmount);
            Assert.Equal(net, row.NetAmount);
            Assert.Equal(order.PaidAmount, row.PaidAmount);
            Assert.Equal(Math.Max(0, net - order.PaidAmount), row.DebtAmount);
            Assert.Equal(order.DiscountAmount, row.DiscountAmount);
            Assert.Equal(order.MaterialCostAmount, row.MaterialCostAmount);
            Assert.Equal(order.LaborCostAmount, row.LaborCostAmount);
            Assert.Equal(net - order.MaterialCostAmount - order.LaborCostAmount, row.ProfitAmount);
            Assert.Equal((315537897599999999L - effectiveDate.Ticks / 10).ToString("D18", CultureInfo.InvariantCulture)
                + ":" + order.Id.ToString("D"), row.FirstKey);
        }

        var expected = await new FinanceQueryOracle(context).GetFinanceSummaryAsync(organizationId, s_testDate.Date, s_testDate.Date);
        var actual = await new OrganizationFinanceQueryService(context).GetFinanceSummaryAsync(organizationId, s_testDate.Date, s_testDate.Date);
        var expectedJson = JsonSerializer.SerializeToNode(expected);
        var actualJson = JsonSerializer.SerializeToNode(actual);

        Assert.True(JsonNode.DeepEquals(expectedJson, actualJson), $"Ожидалось: {expectedJson}\nПолучено: {actualJson}");
    }
}
