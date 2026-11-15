using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data.Repositories;

namespace OpenCRM.Modules.Vehicles.Tests.Integration;

public sealed class VehicleMaintenanceLookupServiceTests(VehicleDatabaseFixture fixture) : IClassFixture<VehicleDatabaseFixture>
{
    [Fact]
    public async Task LookupKeepsArchiveButRejectsDeletedForeignAndPersonalVehiclesWithoutTracking()
    {
        await using var preparation = fixture.CreateContext();
        var owner = new ApplicationUser();
        var organization = new Organization { Name = "Техника обслуживания", OwnerUser = owner };
        var otherOrganization = new Organization { Name = "Другая организация", OwnerUser = owner };
        var client = new OrganizationClient { Organization = organization, Name = "Владелец техники" };
        var archived = new Vehicle { Organization = organization, OrganizationClient = client, User = owner, IsArchived = true };
        var deleted = new Vehicle { Organization = organization, User = owner, IsDeleted = true };
        var foreign = new Vehicle { Organization = otherOrganization, User = owner };
        var personal = new Vehicle { User = owner };
        preparation.Vehicles.AddRange(archived, deleted, foreign, personal);
        await preparation.SaveChangesAsync();
        var capture = new QueryCapture();
        await using var context = fixture.CreateContext(capture);
        var lookup = new VehicleMaintenanceLookupService(new VehicleRepository(context));

        var result = await lookup.FindAsync("сотрудник", organization.Id, archived.Id);

        Assert.Equal(archived.Id, result?.Id);
        Assert.Equal(client.Id, result?.OrganizationClientId);
        var sql = Assert.Single(capture.Commands);
        Assert.Contains("LIMIT", sql);
        Assert.DoesNotContain("ManualBrandName", sql);
        Assert.Null(await lookup.FindAsync(owner.Id, organization.Id, deleted.Id));
        Assert.Null(await lookup.FindAsync(owner.Id, organization.Id, foreign.Id));
        Assert.Null(await lookup.FindAsync(owner.Id, organization.Id, personal.Id));
        Assert.Null(await lookup.FindAsync(owner.Id, null, archived.Id));
        Assert.Null(await lookup.FindAsync("другой пользователь", null, personal.Id));
        Assert.Equal(personal.Id, (await lookup.FindAsync(owner.Id, null, personal.Id))?.Id);
        Assert.Equal(7, capture.Commands.Count);
        Assert.Empty(context.ChangeTracker.Entries());

        var tracked = await new VehicleRepository(context).Get(owner.Id, organization.Id, archived.Id);

        Assert.NotNull(tracked);
        Assert.Equal(EntityState.Unchanged, context.Entry(tracked).State);
    }

    private sealed class QueryCapture : DbCommandInterceptor
    {
        internal List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            this.Commands.Add(command.CommandText);

            return ValueTask.FromResult(result);
        }
    }
}
