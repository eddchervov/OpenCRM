using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Testing;
using Npgsql;

namespace OpenCRM.Modules.Maintenance.Tests.Integration;

public sealed class MaintenanceTransactionTests(MaintenanceDatabaseFixture fixture) : IClassFixture<MaintenanceDatabaseFixture>
{
    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 1)]
    public async Task TransientFailurePreservesOneWriteBeforeOrAfterCommit(bool afterCommit, int expectedAttempts)
    {
        await using var connectionContext = fixture.CreateContext();
        var owner = new ApplicationUser();
        connectionContext.Users.Add(owner);
        await connectionContext.SaveChangesAsync();
        var interceptor = new CommitFailureInterceptor(afterCommit);
        var options = new DbContextOptionsBuilder<OpenCRMContext>()
            .UseNpgsql(connectionContext.Database.GetConnectionString(), database => database.EnableRetryOnFailure())
            .AddInterceptors(interceptor)
            .Options;
        await using var context = new OpenCRMContext(options);
        var id = Guid.NewGuid();
        var attempts = 0;
        var transaction = new MaintenanceTransaction(context);

        var result = await transaction.ExecuteAsync(async () =>
        {
            attempts++;
            Assert.NotNull(context.Database.CurrentTransaction);
            Assert.Empty(context.ChangeTracker.Entries<Organization>());
            context.Organizations.Add(new Organization { Id = id, Name = "Повтор обслуживания", OwnerUserId = owner.Id });
            await context.SaveChangesAsync();

            return id;
        }, () => context.Organizations.AsNoTracking().AnyAsync(value => value.Id == id));

        Assert.Equal(id, result);
        Assert.Equal(expectedAttempts, attempts);
        Assert.Equal(1, await context.Organizations.CountAsync(value => value.Id == id));
        Assert.Null(context.Database.CurrentTransaction);
    }

    [Fact]
    public async Task ExistingTransactionRemainsOwnedByCallerAndCanRollback()
    {
        await using var context = fixture.CreateContext();
        var owner = new ApplicationUser();
        context.Users.Add(owner);
        await context.SaveChangesAsync();
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var outer = await context.Database.BeginTransactionAsync();
            var id = Guid.NewGuid();
            var transaction = new MaintenanceTransaction(context);
            await transaction.ExecuteAsync(async () =>
            {
                Assert.Same(outer, context.Database.CurrentTransaction);
                context.Organizations.Add(new Organization { Id = id, Name = "Внешняя транзакция", OwnerUserId = owner.Id });
                await context.SaveChangesAsync();
            }, () => throw new InvalidOperationException("Проверка результата внешней транзакции не должна вызываться."));

            Assert.Same(outer, context.Database.CurrentTransaction);
            await outer.RollbackAsync();
            context.ChangeTracker.Clear();
            Assert.False(await context.Organizations.AnyAsync(value => value.Id == id));
        });
    }

    private sealed class CommitFailureInterceptor(bool afterCommit) : DbTransactionInterceptor
    {
        private bool _hasFailed;

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (!afterCommit)
            {
                this.FailOnce();
            }

            return ValueTask.FromResult(result);
        }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (afterCommit)
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

            throw new PostgresException("Временный сбой транзакции.", "ERROR", "ERROR", "40001");
        }
    }
}
