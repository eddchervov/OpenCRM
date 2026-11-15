using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Modules.Clients.Contracts;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Clients.Tests.Integration;

[Collection(ClientsDatabaseCollection.Name)]
public sealed class ClientMaintenancePageTests(ClientsDatabaseFixture fixture)
{
    [Fact]
    public async Task PagesCoverEntireHistoryAndFilterBeforePagination()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var client = ClientsDatabaseFixture.CreateClient(organization, "История");
        context.OrganizationClients.Add(client);
        await context.SaveChangesAsync();
        var vehicle = await fixture.AddVehicleAsync(context, organization, client);
        var date = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var rows = Enumerable.Range(0, 25).Select(index =>
        {
            var row = ClientsDatabaseFixture.CreateMaintenance(organization, vehicle,
                index % 2 == 0 ? MaintenanceStatus.Completed : MaintenanceStatus.Planned, date.AddDays(-index));
            row.Details = [new MaintenanceDetail { Name = index == 24 ? "Давняя полировка 100%" : "Мойка", Price = 1000 }];

            return row;
        }).ToArray();
        context.Maintenance.AddRange(rows);
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        var reader = services.GetRequiredService<IOrganizationClientMaintenancePageQueryService>();

        var first = await reader.GetPageAsync(organization.OwnerUserId, organization.Id, client.Id);
        var second = await reader.GetPageAsync(organization.OwnerUserId, organization.Id, client.Id, 2);
        var third = await reader.GetPageAsync(organization.OwnerUserId, organization.Id, client.Id, 3);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotNull(third);
        Assert.Equal(25, first.TotalCount);
        Assert.Equal(10, first.Items.Count);
        Assert.Equal(10, second.Items.Count);
        Assert.Equal(5, third.Items.Count);
        Assert.Equal(rows.Select(row => row.Id), first.Items.Concat(second.Items).Concat(third.Items).Select(row => row.Id));
        var found = await reader.GetPageAsync(organization.OwnerUserId, organization.Id, client.Id,
            searchQuery: "ПОЛИРОВКА 100%");
        Assert.Equal(rows[24].Id, Assert.Single(found!.Items).Id);
        Assert.Equal(1, found.TotalCount);
        var upcoming = await reader.GetPageAsync(organization.OwnerUserId, organization.Id, client.Id, status: "upcoming");
        Assert.Equal(12, upcoming!.TotalCount);
        Assert.All(upcoming.Items, item => Assert.Equal(MaintenanceStatus.Planned, item.Status));
        var completed = await reader.GetPageAsync(organization.OwnerUserId, organization.Id, client.Id, status: "completed");
        Assert.Equal(13, completed!.TotalCount);
        var last = await reader.GetPageAsync(organization.OwnerUserId, organization.Id, client.Id, int.MaxValue);
        Assert.Equal(3, last!.Page);
        var bounded = await reader.GetPageAsync(organization.OwnerUserId, organization.Id, client.Id, -1, 1000);
        Assert.Equal(1, bounded!.Page);
        Assert.Equal(100, bounded.PageSize);
    }

    [Fact]
    public async Task HistoryUsesBuyerAndOrganizationAndPreservesArchivedVehicleHistory()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var otherOrganization = await fixture.CreateOrganizationAsync(context);
        var buyer = ClientsDatabaseFixture.CreateClient(organization, "Покупатель");
        var newOwner = ClientsDatabaseFixture.CreateClient(organization, "Новый владелец");
        context.OrganizationClients.AddRange(buyer, newOwner);
        await context.SaveChangesAsync();
        var vehicle = await fixture.AddVehicleAsync(context, organization, newOwner, "Архивный автомобиль");
        vehicle.IsArchived = true;
        var deletedVehicle = await fixture.AddVehicleAsync(context, organization, buyer, isDeleted: true);
        var foreignVehicle = await fixture.AddVehicleAsync(context, otherOrganization);
        var date = DateTime.UtcNow;
        var visible = ClientsDatabaseFixture.CreateMaintenance(organization, vehicle, MaintenanceStatus.Completed, date);
        visible.BuyerClientId = buyer.Id;
        var deleted = ClientsDatabaseFixture.CreateMaintenance(organization, vehicle, MaintenanceStatus.Completed, date);
        deleted.BuyerClientId = buyer.Id;
        deleted.IsDeleted = true;
        var foreign = ClientsDatabaseFixture.CreateMaintenance(otherOrganization, foreignVehicle, MaintenanceStatus.Completed, date);
        foreign.BuyerClientId = buyer.Id;
        context.Maintenance.AddRange(visible, deleted, foreign,
            ClientsDatabaseFixture.CreateMaintenance(organization, deletedVehicle, MaintenanceStatus.Completed, date),
            ClientsDatabaseFixture.CreateMaintenance(organization, vehicle, MaintenanceStatus.Planned, date));
        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        var reader = services.GetRequiredService<IOrganizationClientMaintenancePageQueryService>();

        var result = await reader.GetPageAsync(organization.OwnerUserId, organization.Id, buyer.Id, searchQuery: "Архивный автомобиль");

        Assert.Equal(visible.Id, Assert.Single(result!.Items).Id);
        Assert.Null(await reader.GetPageAsync(organization.OwnerUserId, otherOrganization.Id, buyer.Id));
        Assert.Null(await reader.GetPageAsync(organization.OwnerUserId, organization.Id, Guid.NewGuid()));
    }

    [Fact]
    public async Task PermissionsProtectMaintenanceAndFinancialData()
    {
        var sql = new ClientsDatabaseFixture.SqlCaptureInterceptor();
        await using var context = fixture.CreateContext(sql);
        var organization = await fixture.CreateOrganizationAsync(context);
        var client = ClientsDatabaseFixture.CreateClient(organization, "Права");
        context.OrganizationClients.Add(client);
        await context.SaveChangesAsync();
        var vehicle = await fixture.AddVehicleAsync(context, organization, client);
        var row = ClientsDatabaseFixture.CreateMaintenance(organization, vehicle, MaintenanceStatus.InProgress, DateTime.UtcNow);
        row.Details = [new MaintenanceDetail { Name = "Полировка", Price = 1500 }];
        row.DiscountAmount = 100;
        context.Maintenance.Add(row);
        context.MaintenancePayments.Add(new MaintenancePayment
        {
            Maintenance = row, Amount = 500, CreatedBy = organization.OwnerUserId, CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        using var allowed = fixture.CreateServices(context);
        var reader = allowed.GetRequiredService<IOrganizationClientMaintenancePageQueryService>();
        var result = await reader.GetPageAsync(organization.OwnerUserId, organization.Id, client.Id, status: "upcoming");
        var item = Assert.Single(result!.Items);
        Assert.Equal(1500m, item.GrossAmount);
        Assert.Equal(1400m, item.NetAmount);
        Assert.Equal(500m, item.PaidAmount);
        using var noFinance = fixture.CreateServices(context, deniedPermissionCodes: new HashSet<string>
        {
            OrganizationPermissionCodes.FinanceView, OrganizationPermissionCodes.FinanceManage
        });
        sql.Commands.Clear();
        var masked = await noFinance.GetRequiredService<IOrganizationClientMaintenancePageQueryService>()
            .GetPageAsync(organization.OwnerUserId, organization.Id, client.Id);
        Assert.Null(Assert.Single(masked!.Items).GrossAmount);
        Assert.Null(Assert.Single(masked.Items).NetAmount);
        Assert.Null(Assert.Single(masked.Items).PaidAmount);
        Assert.DoesNotContain(sql.Commands, command => command.Contains("MaintenancePayments", StringComparison.Ordinal));
        using var noMaintenance = fixture.CreateServices(context, deniedPermissionCodes: new HashSet<string>
        {
            OrganizationPermissionCodes.MaintenanceView
        });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => noMaintenance
            .GetRequiredService<IOrganizationClientMaintenancePageQueryService>()
            .GetPageAsync(organization.OwnerUserId, organization.Id, client.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.GetPageAsync(
            organization.OwnerUserId, organization.Id, client.Id, status: "unknown"));
    }
}
