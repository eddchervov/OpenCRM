using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Clients.Contracts;
using OpenCRM.Modules.Clients.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;
using Npgsql;

namespace OpenCRM.Modules.Clients.Tests.Integration;

[Collection(ClientsDatabaseCollection.Name)]
public sealed class ClientTransactionTests(ClientsDatabaseFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QuickBookingResolutionPreservesOneCreatedClientAfterTransientCommitFailure(bool failAfterCommit)
    {
        var interceptor = new ClientCommitFailureInterceptor(failAfterCommit);
        await using var context = fixture.CreateContext(interceptor);
        var organization = await fixture.CreateOrganizationAsync(context);
        using var services = ClientCommandTestHarness.CreateServices(context);
        var commands = services.GetRequiredService<IOrganizationClientCommandService>();
        interceptor.Arm();

        var result = await commands.ResolveQuickBookingClientAsync(
            organization.OwnerUserId, organization.Id, "+7 (900) 111-22-33", "Клиент записи", "Быстрая запись");
        var reused = await commands.ResolveQuickBookingClientAsync(
            organization.OwnerUserId, organization.Id, "79001112233", "Другое имя", "Быстрая запись");

        Assert.True(result.Created);
        Assert.Equal((result.ClientId, false), reused);
        Assert.Equal(1, interceptor.FailureCount);
        await using var verify = fixture.CreateContext();
        Assert.Single(await verify.OrganizationClients.Where(client => client.OrganizationId == organization.Id).ToListAsync());
        Assert.Single(await verify.OrganizationClientHistoryEvents.Where(item => item.OrganizationId == organization.Id).ToListAsync());
    }

    [Fact]
    public async Task QuickBookingResolutionRequiresClientCreationPermissionOnlyForNewClient()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var existing = ClientsDatabaseFixture.CreateClient(organization, "Существующий клиент");
        existing.Phone = "79001112233";
        context.OrganizationClients.Add(existing);
        await context.SaveChangesAsync();
        using var services = ClientCommandTestHarness.CreateServices(context);
        var subscription = Mock.Get(services.GetRequiredService<IOrganizationSubscriptionService>());
        subscription.Setup(service => service.EnsureWorkspaceCanWriteAsync(
                organization.OwnerUserId, organization.Id, OrganizationPermissionCodes.ClientsCreate))
            .ThrowsAsync(new InvalidOperationException("Создание клиентов запрещено."));
        var commands = services.GetRequiredService<IOrganizationClientCommandService>();

        var reused = await commands.ResolveQuickBookingClientAsync(
            organization.OwnerUserId, organization.Id, "+7 (900) 111-22-33", null, "Быстрая запись");

        Assert.Equal(((Guid?)existing.Id, false), reused);
        await Assert.ThrowsAsync<InvalidOperationException>(() => commands.ResolveQuickBookingClientAsync(
            organization.OwnerUserId, organization.Id, "79009998877", "Новый клиент", "Быстрая запись"));
        Assert.Single(await context.OrganizationClients.Where(client => client.OrganizationId == organization.Id).ToListAsync());
        Assert.False(await context.OrganizationClientHistoryEvents.AnyAsync(item => item.OrganizationId == organization.Id));
    }

    [Fact]
    public async Task EmptyQuickBookingIdentityDoesNotCreateClient()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        using var services = ClientCommandTestHarness.CreateServices(context);
        var commands = services.GetRequiredService<IOrganizationClientCommandService>();

        var result = await commands.ResolveQuickBookingClientAsync(
            organization.OwnerUserId, organization.Id, " ", null, "Быстрая запись");

        Assert.Equal(((Guid?)null, false), result);
        Assert.False(await context.OrganizationClients.AnyAsync(client => client.OrganizationId == organization.Id));
    }

    [Theory]
    [InlineData("create", false)]
    [InlineData("create", true)]
    [InlineData("update_changed", false)]
    [InlineData("update_changed", true)]
    [InlineData("update_noop", false)]
    [InlineData("update_noop", true)]
    [InlineData("unlink", false)]
    [InlineData("unlink", true)]
    [InlineData("transfer", false)]
    [InlineData("transfer", true)]
    public async Task TransientCommitFailurePreservesOneCommandWrite(string operation, bool failAfterCommit)
    {
        var interceptor = new ClientCommitFailureInterceptor(failAfterCommit);
        await using var context = fixture.CreateContext(interceptor);
        var organization = await fixture.CreateOrganizationAsync(context);
        OrganizationClient? source = null;
        OrganizationClient? target = null;
        Vehicle? vehicle = null;

        if (operation.StartsWith("update_", StringComparison.Ordinal))
        {
            source = ClientsDatabaseFixture.CreateClient(organization, "Исходное имя");
            context.OrganizationClients.Add(source);
            await context.SaveChangesAsync();
        }
        else if (operation is "unlink" or "transfer")
        {
            source = ClientsDatabaseFixture.CreateClient(organization, "Исходный клиент");
            context.OrganizationClients.Add(source);
            if (operation == "transfer")
            {
                target = ClientsDatabaseFixture.CreateClient(organization, "Новый клиент");
                context.OrganizationClients.Add(target);
            }

            await context.SaveChangesAsync();
            vehicle = await fixture.AddVehicleAsync(context, organization, source);
        }

        interceptor.Arm();
        using var services = ClientCommandTestHarness.CreateServices(context);
        var commands = services.GetRequiredService<IOrganizationClientCommandService>();
        var links = services.GetRequiredService<IOrganizationClientVehicleLinkService>();
        Guid? createdClientId = null;
        var expectedHistoryCount = operation switch
        {
            "create" => 1,
            "update_changed" => 1,
            "update_noop" => 0,
            "unlink" => 1,
            "transfer" => 1,
            _ => throw new InvalidOperationException($"Неизвестная команда: {operation}")
        };

        switch (operation)
        {
            case "create":
                createdClientId = await commands.CreateAsync(organization.OwnerUserId, organization.Id, new CreateOrganizationClientDto
                {
                    Name = "Созданный клиент",
                    PartyTypeCode = "person",
                    TagNames = ["Повторяемый тег"]
                });
                break;
            case "update_changed":
            case "update_noop":
                await commands.UpdateAsync(organization.OwnerUserId, organization.Id, source!.Id, new UpdateOrganizationClientDto
                {
                    Name = operation == "update_changed" ? "Новое имя" : source.Name,
                    PartyTypeCode = "person"
                });
                break;
            case "unlink":
                await links.UnlinkVehicleAsync(organization.OwnerUserId, organization.Id, source!.Id, vehicle!.Id);
                break;
            case "transfer":
                await links.TransferVehicleAsync(organization.OwnerUserId, organization.Id, source!.Id, vehicle!.Id, target!.Id);
                break;
        }

        var shouldRetryAfterCommit = operation == "update_noop";
        var expectedCommitAttempts = !failAfterCommit || shouldRetryAfterCommit ? 2 : 1;
        Assert.Equal(expectedCommitAttempts, interceptor.CommitAttempts);
        Assert.Equal(1, interceptor.FailureCount);

        var expectedClientId = createdClientId ?? source?.Id;
        if (expectedClientId.HasValue)
        {
            Assert.Equal(expectedHistoryCount, await context.OrganizationClientHistoryEvents.AsNoTracking()
                .CountAsync(item => item.OrganizationClientId == expectedClientId.Value));
            Assert.Equal(operation == "create" || operation == "update_changed" || operation == "unlink" || operation == "transfer" ? 1 : 0,
                await context.OrganizationAuditEvents.AsNoTracking()
                    .CountAsync(item => item.TargetId == expectedClientId.Value.ToString("D")));
        }

        if (operation == "create")
        {
            Assert.Equal(1, await context.OrganizationClients.AsNoTracking()
                .CountAsync(client => client.OrganizationId == organization.Id && client.Name == "Созданный клиент"));
            var tag = await context.OrganizationClientTags.AsNoTracking()
                .SingleAsync(item => item.OrganizationId == organization.Id && item.NormalizedName == "ПОВТОРЯЕМЫЙ ТЕГ");
            Assert.Equal(1, await context.OrganizationClientTagAssignments.AsNoTracking()
                .CountAsync(item => item.OrganizationClientTagId == tag.Id));
        }

        if (operation == "transfer")
        {
            Assert.Equal(target!.Id, await context.Vehicles.AsNoTracking().Where(item => item.Id == vehicle!.Id)
                .Select(item => item.OrganizationClientId).SingleAsync());
            Assert.Single(await context.OrganizationClientHistoryEvents.AsNoTracking()
                .Where(item => item.OrganizationClientId == target.Id && item.RelatedEntityId == vehicle!.Id).ToListAsync());
        }
    }

    private sealed class ClientCommitFailureInterceptor(bool failAfterCommit) : DbTransactionInterceptor
    {
        private bool _armed;
        private bool _hasFailed;

        internal int CommitAttempts { get; private set; }
        internal int FailureCount { get; private set; }

        internal void Arm() => this._armed = true;

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (!this._armed)
            {
                return ValueTask.FromResult(result);
            }

            this.CommitAttempts++;
            if (!failAfterCommit)
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
            if (this._armed && failAfterCommit)
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
            this.FailureCount++;
            throw new PostgresException("Временный сбой фиксации команды клиента.", "ERROR", "ERROR", "40001");
        }
    }
}
