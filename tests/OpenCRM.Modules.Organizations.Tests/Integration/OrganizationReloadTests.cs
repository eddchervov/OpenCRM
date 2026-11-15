using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain;
using OpenCRM.Modules.Organizations.Contracts;

namespace OpenCRM.Modules.Organizations.Tests.Integration;

[Collection(OrganizationsDatabaseCollection.Name)]
public sealed class OrganizationReloadTests(OrganizationsDatabaseFixture fixture)
{
    [Fact]
    public async Task AccessRejectsOrganizationDeletedBetweenLookupAndReload()
    {
        var interceptor = new AfterLookupInterceptor();
        await using var context = fixture.CreateContext(interceptor);
        var organization = await fixture.CreateOrganizationAsync(context);
        interceptor.Arm(async () =>
        {
            await using var concurrent = fixture.CreateContext();
            await concurrent.OrganizationMembers.Where(value => value.OrganizationId == organization.Id).ExecuteDeleteAsync();
            await concurrent.Organizations.Where(value => value.Id == organization.Id).ExecuteDeleteAsync();
        });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new OrganizationAccess(context).GetActiveAsync(organization.Id));

        Assert.Equal("Организация не найдена.", error.Message);
        Assert.Equal(1, interceptor.Calls);
    }

    [Theory]
    [InlineData("deleted")]
    [InlineData("unlinked")]
    [InlineData("relinked")]
    public async Task MemberRemovalDoesNotOverwriteEmployeeChangedBeforeItsLock(string change)
    {
        var interceptor = new BeforeEmployeeLockInterceptor();
        await using var context = fixture.CreateContext(interceptor);
        var organization = await fixture.CreateOrganizationAsync(context);
        var member = await OrganizationCommandHarness.AddMemberAsync(context, organization);
        var anotherMember = await OrganizationCommandHarness.AddMemberAsync(context, organization);
        var employee = new OrganizationEmployee { OrganizationId = organization.Id, MemberId = member.Id, Name = "Мастер" };
        context.OrganizationEmployees.Add(employee);
        await context.SaveChangesAsync();
        interceptor.Arm(async () =>
        {
            await using var concurrent = fixture.CreateContext();
            var employeeQuery = concurrent.OrganizationEmployees.Where(value => value.Id == employee.Id);
            if (change == "deleted")
            {
                await employeeQuery.ExecuteDeleteAsync();
            }
            else
            {
                Guid? newMemberId = change == "relinked" ? anotherMember.Id : null;
                await employeeQuery.ExecuteUpdateAsync(update => update.SetProperty(value => value.MemberId, newMemberId));
            }
        });
        using var services = OrganizationCommandHarness.CreateServices(context);
        await services.GetRequiredService<IOrganizationMembershipService>().RemoveMemberAsync(organization.OwnerUserId, organization.Id, member.Id);

        Assert.Equal(1, interceptor.Calls);
        Assert.False(await context.OrganizationMembers.AsNoTracking().AnyAsync(value => value.Id == member.Id));
        Assert.Equal(1, await context.OrganizationAuditEvents.CountAsync(value => value.OrganizationId == organization.Id));
        if (change == "deleted")
        {
            Assert.False(await context.OrganizationEmployees.AsNoTracking().AnyAsync(value => value.Id == employee.Id));
        }
        else
        {
            Assert.Equal(change == "relinked" ? anotherMember.Id : (Guid?)null,
                await context.OrganizationEmployees.AsNoTracking().Where(value => value.Id == employee.Id)
                    .Select(value => value.MemberId).SingleAsync());
        }
    }

    private sealed class AfterLookupInterceptor : DbCommandInterceptor
    {
        private Func<Task>? _change;

        internal int Calls { get; private set; }

        internal void Arm(Func<Task> change) => this._change = change;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (this._change != null && command.CommandText.Contains("FROM \"Organizations\"", StringComparison.Ordinal))
            {
                var change = this._change;
                this._change = null;
                this.Calls++;
                await change();
            }

            return result;
        }
    }

    private sealed class BeforeEmployeeLockInterceptor : DbCommandInterceptor
    {
        private Func<Task>? _change;

        internal int Calls { get; private set; }

        internal void Arm(Func<Task> change) => this._change = change;

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (this._change != null && command.CommandText.Contains("FROM \"OrganizationEmployees\"", StringComparison.Ordinal)
                && command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal))
            {
                var change = this._change;
                this._change = null;
                this.Calls++;
                await change();
            }

            return result;
        }
    }
}
