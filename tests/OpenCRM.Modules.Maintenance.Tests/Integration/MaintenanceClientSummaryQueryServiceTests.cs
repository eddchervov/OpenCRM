using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;

namespace OpenCRM.Modules.Maintenance.Tests.Integration;

public sealed class MaintenanceClientSummaryQueryServiceTests(MaintenanceDatabaseFixture fixture)
    : IClassFixture<MaintenanceDatabaseFixture>
{
    [Fact]
    public async Task SummariesUseOnlyBuyerAndRespectOrganizationDeletesAndCompletionDates()
    {
        await using var context = fixture.CreateContext();
        var organization = MaintenanceClientSummaryQueryServiceTests.CreateOrganization();
        var owner = MaintenanceClientSummaryQueryServiceTests.CreateClient(organization, "Владелец");
        var buyer = MaintenanceClientSummaryQueryServiceTests.CreateClient(organization, "Покупатель");
        var vehicle = MaintenanceClientSummaryQueryServiceTests.CreateVehicle(organization, owner, isArchived: true);
        var unassignedVehicle = MaintenanceClientSummaryQueryServiceTests.CreateVehicle(organization, null);
        var deletedVehicle = MaintenanceClientSummaryQueryServiceTests.CreateVehicle(organization, owner);
        deletedVehicle.IsDeleted = true;
        var foreignVehicle = MaintenanceClientSummaryQueryServiceTests.CreateVehicle(
            MaintenanceClientSummaryQueryServiceTests.CreateOrganization(), null);
        var firstDate = MaintenanceClientSummaryQueryServiceTests.UtcDate(2026, 1, 1);
        var completedDate = firstDate.AddDays(4);
        var laterDate = firstDate.AddYears(1);
        context.OrganizationClients.AddRange(owner, buyer);
        var deletedOrder = MaintenanceClientSummaryQueryServiceTests.CreateMaintenance(
            vehicle, laterDate, MaintenanceStatus.Completed, buyerClient: buyer);
        deletedOrder.IsDeleted = true;
        context.Maintenance.AddRange(
            MaintenanceClientSummaryQueryServiceTests.CreateMaintenance(
                vehicle, firstDate, MaintenanceStatus.Completed, buyerClient: buyer),
            MaintenanceClientSummaryQueryServiceTests.CreateMaintenance(
                vehicle, laterDate, MaintenanceStatus.Planned, buyerClient: buyer),
            MaintenanceClientSummaryQueryServiceTests.CreateMaintenance(
                unassignedVehicle, firstDate, MaintenanceStatus.Completed, completedDate, buyer),
            MaintenanceClientSummaryQueryServiceTests.CreateMaintenance(
                vehicle, laterDate, MaintenanceStatus.Completed),
            MaintenanceClientSummaryQueryServiceTests.CreateMaintenance(
                deletedVehicle, laterDate, MaintenanceStatus.Completed, buyerClient: buyer),
            MaintenanceClientSummaryQueryServiceTests.CreateMaintenance(
                foreignVehicle, laterDate, MaintenanceStatus.Completed, buyerClient: buyer),
            deletedOrder);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var service = new MaintenanceClientSummaryQueryService(context);

        var result = await service.GetByClientIdsAsync(organization.Id, [owner.Id, buyer.Id, Guid.NewGuid()]);

        Assert.Equal(buyer.Id, Assert.Single(result).Key);
        Assert.Equal(3, result[buyer.Id].Count);
        Assert.Equal(completedDate, result[buyer.Id].LastDate);
        Assert.Equal(3, await service.GetLinkedCountAsync(organization.Id));
        Assert.Empty(await service.GetByClientIdsAsync(organization.Id, [owner.Id]));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task MissingBuyerDoesNotFallBackToVehicleOwnerOrSnapshot()
    {
        await using var context = fixture.CreateContext();
        var organization = MaintenanceClientSummaryQueryServiceTests.CreateOrganization();
        var owner = MaintenanceClientSummaryQueryServiceTests.CreateClient(organization, "Владелец");
        var vehicle = MaintenanceClientSummaryQueryServiceTests.CreateVehicle(organization, owner);
        var order = MaintenanceClientSummaryQueryServiceTests.CreateMaintenance(
            vehicle, MaintenanceClientSummaryQueryServiceTests.UtcDate(2026, 2, 1), MaintenanceStatus.Completed);
        order.BuyerName = "Сохранённые реквизиты";
        context.OrganizationClients.Add(owner);
        context.Maintenance.Add(order);
        await context.SaveChangesAsync();
        var service = new MaintenanceClientSummaryQueryService(context);

        Assert.Empty(await service.GetByClientIdsAsync(organization.Id, [owner.Id]));
        Assert.Equal(0, await service.GetLinkedCountAsync(organization.Id));
    }

    [Fact]
    public async Task SummaryAggregatesInOneSqlAndSkipsEmptyClientIds()
    {
        var interceptor = new ReaderCommandCounter();
        await using var context = fixture.CreateContext(interceptor);
        var organization = MaintenanceClientSummaryQueryServiceTests.CreateOrganization();
        var clients = Enumerable.Range(0, 100)
            .Select(index => MaintenanceClientSummaryQueryServiceTests.CreateClient(organization, $"Клиент {index}"))
            .ToArray();
        context.OrganizationClients.AddRange(clients);
        var vehicle = MaintenanceClientSummaryQueryServiceTests.CreateVehicle(organization, null);
        var date = MaintenanceClientSummaryQueryServiceTests.UtcDate(2026, 3, 1);
        context.Maintenance.AddRange(
            MaintenanceClientSummaryQueryServiceTests.CreateMaintenance(
                vehicle, date, MaintenanceStatus.Completed, buyerClient: clients[0]),
            MaintenanceClientSummaryQueryServiceTests.CreateMaintenance(
                vehicle, date, MaintenanceStatus.Planned, buyerClient: clients[1]));
        await context.SaveChangesAsync();
        interceptor.Reset();
        var service = new MaintenanceClientSummaryQueryService(context);

        Assert.Empty(await service.GetByClientIdsAsync(organization.Id, []));
        Assert.Equal(0, interceptor.ReaderCommandCount);
        var result = await service.GetByClientIdsAsync(organization.Id, clients.Select(client => client.Id).ToArray());

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[clients[0].Id].Count);
        Assert.Equal(date, result[clients[0].Id].LastDate);
        Assert.Null(result[clients[1].Id].LastDate);
        Assert.Equal(1, interceptor.ReaderCommandCount);
        Assert.Contains("GROUP BY", interceptor.LastReaderCommandText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COUNT(", interceptor.LastReaderCommandText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MAX(", interceptor.LastReaderCommandText, StringComparison.OrdinalIgnoreCase);
    }

    private static Organization CreateOrganization()
    {
        var user = MaintenanceClientSummaryQueryServiceTests.CreateUser();
        return new Organization
        {
            Name = "Организация сводки клиентов",
            OwnerUser = user,
            Members = [new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Owner }]
        };
    }

    private static OrganizationClient CreateClient(Organization organization, string name) => new()
    {
        Organization = organization,
        Name = name
    };

    private static Vehicle CreateVehicle(Organization organization, OrganizationClient? owner, bool isArchived = false)
    {
        var vehicle = new Vehicle
        {
            User = organization.OwnerUser,
            Organization = organization,
            OrganizationClient = owner,
            IsArchived = isArchived
        };

        return vehicle;
    }

    private static global::OpenCRM.Domain.Maintenance CreateMaintenance(
        Vehicle vehicle,
        DateTime date,
        MaintenanceStatus status,
        DateTime? completedDate = null,
        OrganizationClient? buyerClient = null) => new global::OpenCRM.Domain.Maintenance
    {
        User = vehicle.User,
        Vehicle = vehicle,
        Type = MaintenanceType.Maintenance,
        Status = status,
        Date = date,
        CompletedDate = completedDate,
        DateTimeCreation = date,
        BuyerClientId = buyerClient?.Id
    };

    private static ApplicationUser CreateUser() => new()
    {
        Email = $"maintenance-client-summary-{Guid.NewGuid():N}@example.test",
        UserName = $"maintenance-client-summary-{Guid.NewGuid():N}"
    };

    private static DateTime UtcDate(int year, int month, int day) => new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    private sealed class ReaderCommandCounter : DbCommandInterceptor
    {
        private int _readerCommandCount;

        public int ReaderCommandCount => this._readerCommandCount;

        public string LastReaderCommandText { get; private set; } = string.Empty;

        public void Reset()
        {
            this._readerCommandCount = 0;
            this.LastReaderCommandText = string.Empty;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref this._readerCommandCount);
            this.LastReaderCommandText = command.CommandText;

            return ValueTask.FromResult(result);
        }
    }
}
