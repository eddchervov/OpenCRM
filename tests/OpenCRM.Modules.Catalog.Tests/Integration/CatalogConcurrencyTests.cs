using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Catalog.Contracts;
using OpenCRM.Modules.Catalog.Contracts.Dtos;
using Npgsql;

namespace OpenCRM.Modules.Catalog.Tests.Integration;

[Collection(CatalogDatabaseCollection.Name)]
public sealed class CatalogConcurrencyTests(CatalogDatabaseFixture fixture)
{
    [Fact]
    public async Task ConcurrentInitializationCreatesOnlyOneSetOfOrganizationDefaults()
    {
        await using var seedContext = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seedContext);
        await using var firstContext = fixture.CreateContext();
        await using var secondContext = fixture.CreateContext();
        using var firstServices = fixture.CreateServices(firstContext);
        using var secondServices = fixture.CreateServices(secondContext);
        using var firstScope = firstServices.CreateScope();
        using var secondScope = secondServices.CreateScope();
        var firstCommand = firstScope.ServiceProvider.GetRequiredService<ICatalogInitializationService>();
        var secondCommand = secondScope.ServiceProvider.GetRequiredService<ICatalogInitializationService>();

        var firstTask = firstCommand.EnsureDefaultsAsync(organization.Id);
        var secondTask = secondCommand.EnsureDefaultsAsync(organization.Id);
        await Task.WhenAll(firstTask, secondTask);

        await using var verificationContext = fixture.CreateContext();
        var catalogItems = await verificationContext.OrganizationScenarioCatalogItems.AsNoTracking()
            .Where(item => item.OrganizationId == organization.Id)
            .ToListAsync();
        Assert.NotEmpty(catalogItems);
        Assert.Equal(catalogItems.Count, catalogItems.Select(item => (item.KindCode, item.Code)).Distinct().Count());
    }

    [Fact]
    public async Task ConcurrentSameNamedWorkPostsCreateOnlyOneActivePost()
    {
        await using var seedContext = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seedContext);
        var name = $"QA Пост {Guid.NewGuid():N}";
        await using var firstContext = fixture.CreateContext();
        await using var secondContext = fixture.CreateContext();
        using var firstServices = fixture.CreateServices(firstContext);
        using var secondServices = fixture.CreateServices(secondContext);
        using var firstScope = firstServices.CreateScope();
        using var secondScope = secondServices.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<ICatalogCommandService>();
        var second = secondScope.ServiceProvider.GetRequiredService<ICatalogCommandService>();

        var results = await Task.WhenAll(CreateAsync(first), CreateAsync(second));

        Assert.Single(results, result => result == null);
        Assert.Single(results, result => result?.Contains("Пост с таким названием уже есть") == true);
        await using var verify = fixture.CreateContext();
        Assert.Equal(1, await verify.OrganizationScenarioCatalogItems.CountAsync(item =>
            item.OrganizationId == organization.Id && item.KindCode == OrganizationScenarioCatalogKindCodes.WorkPost && item.Name == name));

        async Task<string?> CreateAsync(ICatalogCommandService command)
        {
            try
            {
                await command.CreateAsync(organization.OwnerUserId, organization.Id,
                    new CreateOrganizationScenarioCatalogItemDto { KindCode = OrganizationScenarioCatalogKindCodes.WorkPost, Name = name });

                return null;
            }
            catch (InvalidOperationException exception)
            {
                return exception.Message;
            }
        }
    }

    [Fact]
    public async Task ConcurrentCreatesWithSameNameReceiveDistinctStableCodes()
    {
        await using var seedContext = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seedContext);
        var barrier = new CatalogOrganizationLockBarrier();
        await using var firstContext = fixture.CreateContext(barrier);
        await using var secondContext = fixture.CreateContext(barrier);
        using var firstServices = fixture.CreateServices(firstContext);
        using var secondServices = fixture.CreateServices(secondContext);
        using var firstScope = firstServices.CreateScope();
        using var secondScope = secondServices.CreateScope();
        var firstCommand = firstScope.ServiceProvider.GetRequiredService<ICatalogCommandService>();
        var secondCommand = secondScope.ServiceProvider.GetRequiredService<ICatalogCommandService>();
        var firstTask = firstCommand.CreateAsync(organization.OwnerUserId, organization.Id, CreateSameNamedService());
        var secondTask = secondCommand.CreateAsync(organization.OwnerUserId, organization.Id, CreateSameNamedService());

        await barrier.WaitForBothCommandsAsync();
        barrier.Release();
        var createdItems = await Task.WhenAll(firstTask, secondTask);

        Assert.Equal(2, createdItems.Select(item => item.Id).Distinct().Count());
        Assert.Equal(["общая-услуга", "общая-услуга-2"], createdItems.Select(item => item.Code).Order().ToArray());
        await using var verificationContext = fixture.CreateContext();
        Assert.Equal(2, await verificationContext.OrganizationScenarioCatalogItems.CountAsync(item =>
            item.OrganizationId == organization.Id && item.KindCode == OrganizationScenarioCatalogKindCodes.Service
            && item.Name == "Общая услуга"));
        Assert.Equal(2, await verificationContext.OrganizationAuditEvents.CountAsync(item =>
            item.OrganizationId == organization.Id && item.EventType == "catalog.item.created"));
    }

    [Fact]
    public async Task VehicleTransferWaitsForConcurrentWorkPostCreationAndMarksItsHistoricalUsage()
    {
        await using var seedContext = fixture.CreateContext();
        var sourceOrganization = await fixture.CreateOrganizationAsync(seedContext);
        var targetOrganization = await fixture.CreateOrganizationAsync(seedContext);
        var vehicle = new Vehicle { User = sourceOrganization.OwnerUser, Organization = sourceOrganization };
        seedContext.Maintenance.Add(new OpenCRM.Domain.Maintenance
        {
            User = sourceOrganization.OwnerUser,
            Vehicle = vehicle,
            Date = DateTime.UtcNow,
            Location = "\t ПОСТ ПЕРЕНОСА \r\n",
            Details = []
        });
        await seedContext.SaveChangesAsync();
        await using var creationContext = fixture.CreateContext();
        await using var transferContext = fixture.CreateContext();
        await transferContext.Database.OpenConnectionAsync();
        var transferConnection = (NpgsqlConnection)transferContext.Database.GetDbConnection();
        using var services = fixture.CreateServices(creationContext);
        using var scope = services.CreateScope();
        var command = scope.ServiceProvider.GetRequiredService<ICatalogCommandService>();
        Guid postId = default;

        await creationContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await creationContext.Database.BeginTransactionAsync();
            var post = await command.CreateAsync(targetOrganization.OwnerUserId, targetOrganization.Id,
                new CreateOrganizationScenarioCatalogItemDto
                {
                    KindCode = OrganizationScenarioCatalogKindCodes.WorkPost,
                    Name = "Пост переноса"
                });

            postId = post.Id;
            Assert.False(post.IsUsed);
            var transfer = transferContext.Vehicles.Where(item => item.Id == vehicle.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.OrganizationId, targetOrganization.Id));

            await CatalogConcurrencyTests.WaitForDatabaseLockAsync(seedContext, transferConnection.ProcessID);
            Assert.False(transfer.IsCompleted);
            await transaction.CommitAsync();
            Assert.Equal(1, await transfer.WaitAsync(TimeSpan.FromSeconds(15)));
        });

        await using var verificationContext = fixture.CreateContext();
        Assert.True(await verificationContext.OrganizationScenarioCatalogItems
            .Where(item => item.Id == postId).Select(item => item.HasBeenUsed).SingleAsync());
    }

    private static async Task WaitForDatabaseLockAsync(OpenCRMContext context, int processId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var isWaiting = await context.Database.SqlQuery<bool>($"""
                SELECT EXISTS (
                    SELECT 1 FROM pg_stat_activity WHERE pid = {processId} AND wait_event_type = 'Lock'
                ) AS "Value"
                """).SingleAsync();

            if (isWaiting)
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("Перенос техники не дождался блокировки организации создаваемым постом.");
    }

    private static CreateOrganizationScenarioCatalogItemDto CreateSameNamedService() => new()
    {
        KindCode = OrganizationScenarioCatalogKindCodes.Service,
        Name = "Общая услуга"
    };

    private sealed class CatalogOrganizationLockBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _bothCommands = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _commandCount;

        internal Task WaitForBothCommandsAsync() => this._bothCommands.Task.WaitAsync(TimeSpan.FromSeconds(15));

        internal void Release() => this._release.TrySetResult();

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FOR NO KEY UPDATE", StringComparison.Ordinal))
            {
                if (Interlocked.Increment(ref this._commandCount) == 2)
                {
                    this._bothCommands.TrySetResult();
                }

                await this._release.Task.WaitAsync(cancellationToken);
            }

            return result;
        }
    }
}
