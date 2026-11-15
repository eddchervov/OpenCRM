using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain;
using OpenCRM.Modules.Catalog.Contracts;
using OpenCRM.Modules.Catalog.Contracts.Dtos;
using Npgsql;

namespace OpenCRM.Modules.Catalog.Tests.Integration;

[Collection(CatalogDatabaseCollection.Name)]
public sealed class CatalogTransactionTests(CatalogDatabaseFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommitRetryKeepsExactlyOneCatalogItemAndAudit(bool failAfterCommit)
    {
        var failure = new CatalogCommitFailureInterceptor(failAfterCommit);
        await using var context = fixture.CreateContext(failure);
        var organization = await fixture.CreateOrganizationAsync(context);
        using var services = fixture.CreateServices(context);
        using var scope = services.CreateScope();
        var command = scope.ServiceProvider.GetRequiredService<ICatalogCommandService>();
        failure.Arm();

        var result = await command.CreateAsync(organization.OwnerUserId, organization.Id, new CreateOrganizationScenarioCatalogItemDto
        {
            KindCode = OrganizationScenarioCatalogKindCodes.Service,
            Name = "Повтор фиксации"
        });

        Assert.Equal(1, failure.Failures);
        Assert.Equal(failAfterCommit ? 1 : 2, failure.CommitAttempts);
        await using var verificationContext = fixture.CreateContext();
        var savedItem = await verificationContext.OrganizationScenarioCatalogItems.AsNoTracking().SingleAsync(item => item.Id == result.Id);
        var auditEvents = await verificationContext.OrganizationAuditEvents.AsNoTracking()
            .Where(item => item.OrganizationId == organization.Id)
            .ToListAsync();
        Assert.Equal("Повтор фиксации", savedItem.Name);
        Assert.Single(auditEvents);
        Assert.Equal("catalog.item.created", auditEvents[0].EventType);
        Assert.Equal(result.Id.ToString("D"), auditEvents[0].TargetId);
    }

    [Fact]
    public async Task AmbientTransactionControlsCatalogCommandCommitAndRollback()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);

        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            using var services = fixture.CreateServices(context);
            using var scope = services.CreateScope();
            var command = scope.ServiceProvider.GetRequiredService<ICatalogCommandService>();
            await command.CreateAsync(organization.OwnerUserId, organization.Id, new CreateOrganizationScenarioCatalogItemDto
            {
                KindCode = OrganizationScenarioCatalogKindCodes.Service,
                Name = "Откат внешней транзакцией"
            });

            Assert.Same(transaction, context.Database.CurrentTransaction);
            await transaction.RollbackAsync();
        });

        await using var verificationContext = fixture.CreateContext();
        Assert.False(await verificationContext.OrganizationScenarioCatalogItems.AnyAsync(item =>
            item.OrganizationId == organization.Id && item.Name == "Откат внешней транзакцией"));
        Assert.False(await verificationContext.OrganizationAuditEvents.AnyAsync(item => item.OrganizationId == organization.Id));
    }

    private sealed class CatalogCommitFailureInterceptor(bool failAfterCommit) : DbTransactionInterceptor
    {
        private bool _isArmed;
        private bool _hasFailed;

        internal int CommitAttempts { get; private set; }
        internal int Failures { get; private set; }

        internal void Arm() => this._isArmed = true;

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (this._isArmed)
            {
                this.CommitAttempts++;
                if (!failAfterCommit)
                {
                    this.FailOnce();
                }
            }

            return ValueTask.FromResult(result);
        }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (this._isArmed && failAfterCommit && !this._hasFailed)
            {
                this.FailOnce();
            }

            return Task.CompletedTask;
        }

        private void FailOnce()
        {
            if (this._hasFailed)
            {
                return;
            }

            this._hasFailed = true;
            this.Failures++;
            throw new PostgresException("Тестовый сбой фиксации каталога.", "ERROR", "ERROR", "40001");
        }
    }
}
