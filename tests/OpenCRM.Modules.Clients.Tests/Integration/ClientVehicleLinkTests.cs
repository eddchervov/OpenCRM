using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain;
using OpenCRM.Modules.Clients.Contracts;

namespace OpenCRM.Modules.Clients.Tests.Integration;

[Collection(ClientsDatabaseCollection.Name)]
public sealed class ClientVehicleLinkTests(ClientsDatabaseFixture fixture)
{
    [Fact]
    public async Task UnlinksVehicleAndWritesHistoryAndAuditTogether()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var client = ClientsDatabaseFixture.CreateClient(organization, "Клиент");
        var target = ClientsDatabaseFixture.CreateClient(organization, "Получатель");
        context.OrganizationClients.AddRange(client, target);
        await context.SaveChangesAsync();
        var vehicle = await fixture.AddVehicleAsync(context, organization, client, "Тестовый автомобиль");
        using var services = ClientCommandTestHarness.CreateServices(context);
        var command = services.GetRequiredService<IOrganizationClientVehicleLinkService>();

        await command.UnlinkVehicleAsync(organization.OwnerUserId, organization.Id, client.Id, vehicle.Id);

        var updatedVehicle = await context.Vehicles.AsNoTracking().SingleAsync(item => item.Id == vehicle.Id);
        var history = await context.OrganizationClientHistoryEvents.AsNoTracking().SingleAsync(item => item.RelatedEntityId == vehicle.Id);
        var audit = await context.OrganizationAuditEvents.AsNoTracking().SingleAsync(item => item.TargetId == client.Id.ToString("D"));
        Assert.Null(updatedVehicle.OrganizationClientId);
        Assert.Equal("vehicle_unlinked", history.EventType);
        Assert.Equal("vehicle", history.RelatedEntityType);
        Assert.Equal("clients.vehicle.unlinked", audit.EventType);
    }

    [Fact]
    public async Task TransfersVehicleAndWritesBothClientHistoryEventsAndOneAudit()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var source = ClientsDatabaseFixture.CreateClient(organization, "Исходный клиент");
        var target = ClientsDatabaseFixture.CreateClient(organization, "Новый клиент");
        context.OrganizationClients.AddRange(source, target);
        await context.SaveChangesAsync();
        var vehicle = await fixture.AddVehicleAsync(context, organization, source, "Тестовый автомобиль");
        using var services = ClientCommandTestHarness.CreateServices(context);
        var command = services.GetRequiredService<IOrganizationClientVehicleLinkService>();

        await command.TransferVehicleAsync(organization.OwnerUserId, organization.Id, source.Id, vehicle.Id, target.Id);

        Assert.Equal(target.Id, await context.Vehicles.AsNoTracking().Where(item => item.Id == vehicle.Id)
            .Select(item => item.OrganizationClientId).SingleAsync());
        var history = await context.OrganizationClientHistoryEvents.AsNoTracking()
            .Where(item => item.RelatedEntityId == vehicle.Id)
            .OrderBy(item => item.EventType)
            .ToArrayAsync();
        Assert.Equal(new[] { "vehicle_transferred_in", "vehicle_transferred_out" }, history.Select(item => item.EventType));
        Assert.Equal(source.Id, history.Single(item => item.EventType == "vehicle_transferred_out").OrganizationClientId);
        Assert.Equal(target.Id, history.Single(item => item.EventType == "vehicle_transferred_in").OrganizationClientId);
        Assert.Single(await context.OrganizationAuditEvents.AsNoTracking().Where(item => item.TargetId == source.Id.ToString("D")).ToListAsync());
    }

    [Fact]
    public async Task AllowsTransferFromArchivedSourceAndKeepsOtherArchivedVehicleLinks()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var archivedSource = ClientsDatabaseFixture.CreateClient(organization, "Архивный клиент");
        archivedSource.Delete(DateTime.UtcNow);
        var target = ClientsDatabaseFixture.CreateClient(organization, "Активный клиент");
        context.OrganizationClients.AddRange(archivedSource, target);
        await context.SaveChangesAsync();
        var vehicle = await fixture.AddVehicleAsync(context, organization, archivedSource);
        using var services = ClientCommandTestHarness.CreateServices(context);
        var command = services.GetRequiredService<IOrganizationClientVehicleLinkService>();

        await command.TransferVehicleAsync(organization.OwnerUserId, organization.Id, archivedSource.Id, vehicle.Id, target.Id);

        Assert.Equal(target.Id, await context.Vehicles.AsNoTracking().Where(item => item.Id == vehicle.Id)
            .Select(item => item.OrganizationClientId).SingleAsync());
        Assert.True(await context.OrganizationClients.AsNoTracking().Where(item => item.Id == archivedSource.Id)
            .Select(item => item.IsDeleted).SingleAsync());
    }

    [Fact]
    public async Task RejectsTargetArchivedAfterPreflightAndVehicleLock()
    {
        await using var seedContext = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seedContext);
        var source = ClientsDatabaseFixture.CreateClient(organization, "Исходный клиент");
        var target = ClientsDatabaseFixture.CreateClient(organization, "Получатель");
        seedContext.OrganizationClients.AddRange(source, target);
        await seedContext.SaveChangesAsync();
        var vehicle = await fixture.AddVehicleAsync(seedContext, organization, source);
        await using var context = fixture.CreateContext(new ArchiveClientBeforeVehicleLockInterceptor(fixture, target.Id));
        using var services = ClientCommandTestHarness.CreateServices(context);
        var command = services.GetRequiredService<IOrganizationClientVehicleLinkService>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => command.TransferVehicleAsync(
            organization.OwnerUserId, organization.Id, source.Id, vehicle.Id, target.Id));

        Assert.Equal("Новый клиент не найден.", exception.Message);
        await using var verificationContext = fixture.CreateContext();
        Assert.True(await verificationContext.OrganizationClients.AsNoTracking().Where(item => item.Id == target.Id)
            .Select(item => item.IsDeleted).SingleAsync());
        Assert.Equal(source.Id, await verificationContext.Vehicles.AsNoTracking().Where(item => item.Id == vehicle.Id)
            .Select(item => item.OrganizationClientId).SingleAsync());
        Assert.Empty(await verificationContext.OrganizationClientHistoryEvents.AsNoTracking()
            .Where(item => item.RelatedEntityId == vehicle.Id).ToListAsync());
    }

    [Theory]
    [InlineData(VehicleMutation.OrganizationNull)]
    [InlineData(VehicleMutation.OrganizationChanged)]
    [InlineData(VehicleMutation.ClientNull)]
    [InlineData(VehicleMutation.ClientChanged)]
    [InlineData(VehicleMutation.Deleted)]
    public async Task UnlinkReloadsVehicleAfterLockAndRejectsConcurrentOwnershipChanges(VehicleMutation mutation)
    {
        await using var seedContext = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seedContext);
        var foreignOrganization = await fixture.CreateOrganizationAsync(seedContext, "Другая организация");
        var source = ClientsDatabaseFixture.CreateClient(organization, "Исходный клиент");
        var otherClient = ClientsDatabaseFixture.CreateClient(organization, "Другой клиент");
        seedContext.OrganizationClients.AddRange(source, otherClient);
        await seedContext.SaveChangesAsync();
        var vehicle = await fixture.AddVehicleAsync(seedContext, organization, source);
        var interceptor = new ChangeVehicleBeforeLockInterceptor(fixture, vehicle.Id, foreignOrganization.Id, otherClient.Id, mutation);
        await using var context = fixture.CreateContext(interceptor);
        using var services = ClientCommandTestHarness.CreateServices(context);
        var command = services.GetRequiredService<IOrganizationClientVehicleLinkService>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => command.UnlinkVehicleAsync(
            organization.OwnerUserId, organization.Id, source.Id, vehicle.Id));

        Assert.Equal("Техника клиента не найдена.", exception.Message);
        await using var verificationContext = fixture.CreateContext();
        var currentVehicle = await verificationContext.Vehicles.AsNoTracking().SingleAsync(item => item.Id == vehicle.Id);
        switch (mutation)
        {
            case VehicleMutation.OrganizationNull:
                Assert.Null(currentVehicle.OrganizationId);
                break;
            case VehicleMutation.OrganizationChanged:
                Assert.Equal(foreignOrganization.Id, currentVehicle.OrganizationId);
                break;
            case VehicleMutation.ClientNull:
                Assert.Null(currentVehicle.OrganizationClientId);
                break;
            case VehicleMutation.ClientChanged:
                Assert.Equal(otherClient.Id, currentVehicle.OrganizationClientId);
                break;
            case VehicleMutation.Deleted:
                Assert.True(currentVehicle.IsDeleted);
                break;
        }

        Assert.Empty(await verificationContext.OrganizationClientHistoryEvents.AsNoTracking()
            .Where(item => item.RelatedEntityId == vehicle.Id).ToListAsync());
    }

    [Fact]
    public async Task RejectsVehicleWhoseClientBelongsToAnotherOrganization()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var foreignOrganization = await fixture.CreateOrganizationAsync(context, "Чужая организация");
        var foreignClient = ClientsDatabaseFixture.CreateClient(foreignOrganization, "Чужой клиент");
        var target = ClientsDatabaseFixture.CreateClient(organization, "Локальный клиент");
        context.OrganizationClients.AddRange(foreignClient, target);
        await context.SaveChangesAsync();
        var vehicle = new Vehicle
        {
            OrganizationId = organization.Id,
            OrganizationClientId = foreignClient.Id,
            UserId = organization.OwnerUserId,
            ManualBrandName = "Тест",
            ManualModelName = "Модель",
            DateTimeCreation = DateTime.UtcNow
        };
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();
        using var services = ClientCommandTestHarness.CreateServices(context);
        var command = services.GetRequiredService<IOrganizationClientVehicleLinkService>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => command.TransferVehicleAsync(
            organization.OwnerUserId, organization.Id, foreignClient.Id, vehicle.Id, target.Id));

        Assert.Equal("Техника клиента не найдена.", exception.Message);
        Assert.Equal(foreignClient.Id, await context.Vehicles.AsNoTracking().Where(item => item.Id == vehicle.Id)
            .Select(item => item.OrganizationClientId).SingleAsync());
    }

    [Fact]
    public async Task UsesCatalogBrandModelYearAndNameWhenBuildingVehicleHistoryText()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var client = ClientsDatabaseFixture.CreateClient(organization, "Клиент");
        var target = ClientsDatabaseFixture.CreateClient(organization, "Получатель");
        var brand = new Brand { Name = "Волга" };
        var model = new Model { Name = "Сайбер", Brand = brand };
        context.OrganizationClients.AddRange(client, target);
        context.Models.Add(model);
        await context.SaveChangesAsync();
        var vehicle = new Vehicle
        {
            OrganizationId = organization.Id,
            OrganizationClientId = client.Id,
            UserId = organization.OwnerUserId,
            BrandId = brand.Id,
            ModelId = model.Id,
            YearIssue = 2019,
            Name = "Рабочая машина",
            RegistrationPlate = "А123АА154",
            DateTimeCreation = DateTime.UtcNow
        };
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();
        using var services = ClientCommandTestHarness.CreateServices(context);
        var command = services.GetRequiredService<IOrganizationClientVehicleLinkService>();

        await command.TransferVehicleAsync(organization.OwnerUserId, organization.Id, client.Id, vehicle.Id, target.Id);
        await command.UnlinkVehicleAsync(organization.OwnerUserId, organization.Id, target.Id, vehicle.Id);

        var descriptions = await context.OrganizationClientHistoryEvents.AsNoTracking()
            .Where(item => item.RelatedEntityId == vehicle.Id)
            .Select(item => new { item.EventType, item.Description })
            .ToListAsync();
        Assert.Contains("vehicle_transferred_out", descriptions.Select(item => item.EventType));
        Assert.Contains("vehicle_unlinked", descriptions.Select(item => item.EventType));
        Assert.All(descriptions, item =>
        {
            Assert.Contains("Волга Сайбер", item.Description);
            Assert.Contains("2019 г.", item.Description);
            Assert.Contains("Рабочая машина", item.Description);
            Assert.Contains("А123АА154", item.Description);
        });
    }

    [Fact]
    public async Task IncludesRegistrationPlateOnlyOnceWhenBrandAndModelAreUnknown()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var client = ClientsDatabaseFixture.CreateClient(organization, "Клиент");
        var target = ClientsDatabaseFixture.CreateClient(organization, "Получатель");
        context.OrganizationClients.AddRange(client, target);
        await context.SaveChangesAsync();
        var vehicle = new Vehicle
        {
            OrganizationId = organization.Id,
            OrganizationClientId = client.Id,
            UserId = organization.OwnerUserId,
            RegistrationPlate = "А123АА154",
            DateTimeCreation = DateTime.UtcNow
        };
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();
        using var services = ClientCommandTestHarness.CreateServices(context);
        var command = services.GetRequiredService<IOrganizationClientVehicleLinkService>();

        await command.TransferVehicleAsync(organization.OwnerUserId, organization.Id, client.Id, vehicle.Id, target.Id);
        await command.UnlinkVehicleAsync(organization.OwnerUserId, organization.Id, target.Id, vehicle.Id);

        var descriptions = await context.OrganizationClientHistoryEvents.AsNoTracking()
            .Where(item => item.RelatedEntityId == vehicle.Id)
            .Select(item => item.Description)
            .ToListAsync();
        Assert.Equal(3, descriptions.Count);
        Assert.All(descriptions, description =>
        {
            Assert.Contains("Автомобиль · А123АА154", description);
            Assert.DoesNotContain("А123АА154А123АА154", description);
        });
    }

    [Fact]
    public async Task RejectsSameClientMissingVehicleForeignVehicleAndUnavailableTarget()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var foreignOrganization = await fixture.CreateOrganizationAsync(context, "Другая организация");
        var source = ClientsDatabaseFixture.CreateClient(organization, "Исходный клиент");
        var target = ClientsDatabaseFixture.CreateClient(organization, "Новый клиент");
        var foreignClient = ClientsDatabaseFixture.CreateClient(foreignOrganization, "Чужой клиент");
        context.OrganizationClients.AddRange(source, target, foreignClient);
        await context.SaveChangesAsync();
        var foreignVehicle = await fixture.AddVehicleAsync(context, foreignOrganization, foreignClient);
        using var services = ClientCommandTestHarness.CreateServices(context);
        var command = services.GetRequiredService<IOrganizationClientVehicleLinkService>();

        var sameClient = await Assert.ThrowsAsync<InvalidOperationException>(() => command.TransferVehicleAsync(
            organization.OwnerUserId, organization.Id, source.Id, Guid.NewGuid(), source.Id));
        var missingTarget = await Assert.ThrowsAsync<InvalidOperationException>(() => command.TransferVehicleAsync(
            organization.OwnerUserId, organization.Id, source.Id, Guid.NewGuid(), Guid.NewGuid()));
        var missingVehicle = await Assert.ThrowsAsync<InvalidOperationException>(() => command.UnlinkVehicleAsync(
            organization.OwnerUserId, organization.Id, source.Id, Guid.NewGuid()));
        var foreignVehicleError = await Assert.ThrowsAsync<InvalidOperationException>(() => command.UnlinkVehicleAsync(
            organization.OwnerUserId, organization.Id, source.Id, foreignVehicle.Id));
        var foreignSourceError = await Assert.ThrowsAsync<InvalidOperationException>(() => command.TransferVehicleAsync(
            organization.OwnerUserId, organization.Id, source.Id, foreignVehicle.Id, target.Id));

        Assert.Equal("Выберите другого клиента.", sameClient.Message);
        Assert.Equal("Новый клиент не найден.", missingTarget.Message);
        Assert.Equal("Техника клиента не найдена.", missingVehicle.Message);
        Assert.Equal("Техника клиента не найдена.", foreignVehicleError.Message);
        Assert.Equal("Техника клиента не найдена.", foreignSourceError.Message);
    }

    [Fact]
    public async Task ConcurrentTransfersReloadAfterVehicleLockAndOnlyOneCanMoveTheVehicle()
    {
        await using var seedContext = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seedContext);
        var source = ClientsDatabaseFixture.CreateClient(organization, "Исходный клиент");
        var firstTarget = ClientsDatabaseFixture.CreateClient(organization, "Первый получатель");
        var secondTarget = ClientsDatabaseFixture.CreateClient(organization, "Второй получатель");
        seedContext.OrganizationClients.AddRange(source, firstTarget, secondTarget);
        await seedContext.SaveChangesAsync();
        var vehicle = await fixture.AddVehicleAsync(seedContext, organization, source);
        var barrier = new TwoVehicleLockBarrier();
        await using var firstContext = fixture.CreateContext(barrier);
        await using var secondContext = fixture.CreateContext(barrier);
        using var firstServices = ClientCommandTestHarness.CreateServices(firstContext);
        using var secondServices = ClientCommandTestHarness.CreateServices(secondContext);
        var firstCommand = firstServices.GetRequiredService<IOrganizationClientVehicleLinkService>();
        var secondCommand = secondServices.GetRequiredService<IOrganizationClientVehicleLinkService>();

        var firstTask = CaptureTransferAsync(firstCommand, organization.OwnerUserId, organization.Id, source.Id, vehicle.Id, firstTarget.Id);
        var secondTask = CaptureTransferAsync(secondCommand, organization.OwnerUserId, organization.Id, source.Id, vehicle.Id, secondTarget.Id);
        await barrier.WaitForBothCommandsAsync();
        barrier.Release();
        var results = await Task.WhenAll(firstTask, secondTask);

        Assert.Single(results, result => result);
        Assert.Single(results, result => !result);
        await using var verificationContext = fixture.CreateContext();
        var assignedClientId = await verificationContext.Vehicles.AsNoTracking().Where(item => item.Id == vehicle.Id)
            .Select(item => item.OrganizationClientId).SingleAsync();
        Assert.Contains(assignedClientId, new Guid?[] { firstTarget.Id, secondTarget.Id });
        Assert.Equal(2, await verificationContext.OrganizationClientHistoryEvents.AsNoTracking()
            .CountAsync(item => item.RelatedEntityId == vehicle.Id));
    }

    [Fact]
    public async Task InvalidHistoryForeignKeyRollsBackVehicleLinkHistoryAndAuditTogether()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var source = ClientsDatabaseFixture.CreateClient(organization, "Исходный клиент");
        var target = ClientsDatabaseFixture.CreateClient(organization, "Новый клиент");
        context.OrganizationClients.AddRange(source, target);
        await context.SaveChangesAsync();
        var vehicle = await fixture.AddVehicleAsync(context, organization, source);
        using var services = ClientCommandTestHarness.CreateServices(context);
        var command = services.GetRequiredService<IOrganizationClientVehicleLinkService>();

        await Assert.ThrowsAsync<DbUpdateException>(() => command.TransferVehicleAsync(
            "missing-user", organization.Id, source.Id, vehicle.Id, target.Id));

        await using var verificationContext = fixture.CreateContext();
        Assert.Equal(source.Id, await verificationContext.Vehicles.AsNoTracking().Where(item => item.Id == vehicle.Id)
            .Select(item => item.OrganizationClientId).SingleAsync());
        Assert.Empty(await verificationContext.OrganizationClientHistoryEvents.AsNoTracking()
            .Where(item => item.RelatedEntityId == vehicle.Id).ToListAsync());
        Assert.Empty(await verificationContext.OrganizationAuditEvents.AsNoTracking()
            .Where(item => item.TargetId == source.Id.ToString("D")).ToListAsync());
    }

    [Fact]
    public async Task AmbientTransactionRemainsOwnedByCallerAndCanRollbackCompletedTransfer()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var source = ClientsDatabaseFixture.CreateClient(organization, "Исходный клиент");
        var target = ClientsDatabaseFixture.CreateClient(organization, "Новый клиент");
        context.OrganizationClients.AddRange(source, target);
        await context.SaveChangesAsync();
        var vehicle = await fixture.AddVehicleAsync(context, organization, source);
        using var services = ClientCommandTestHarness.CreateServices(context);
        var command = services.GetRequiredService<IOrganizationClientVehicleLinkService>();
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            await command.TransferVehicleAsync(organization.OwnerUserId, organization.Id, source.Id, vehicle.Id, target.Id);
            await transaction.RollbackAsync();
        });

        await using var verificationContext = fixture.CreateContext();
        Assert.Equal(source.Id, await verificationContext.Vehicles.AsNoTracking().Where(item => item.Id == vehicle.Id)
            .Select(item => item.OrganizationClientId).SingleAsync());
        Assert.Empty(await verificationContext.OrganizationClientHistoryEvents.AsNoTracking()
            .Where(item => item.RelatedEntityId == vehicle.Id).ToListAsync());
        Assert.Empty(await verificationContext.OrganizationAuditEvents.AsNoTracking()
            .Where(item => item.TargetId == source.Id.ToString("D")).ToListAsync());
    }

    private static async Task<bool> CaptureTransferAsync(
        IOrganizationClientVehicleLinkService command,
        string userId,
        Guid organizationId,
        Guid sourceClientId,
        Guid vehicleId,
        Guid targetClientId)
    {
        try
        {
            await command.TransferVehicleAsync(userId, organizationId, sourceClientId, vehicleId, targetClientId);

            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}

internal sealed class TwoVehicleLockBarrier : DbCommandInterceptor
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
        if (command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal))
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

public enum VehicleMutation
{
    OrganizationNull,
    OrganizationChanged,
    ClientNull,
    ClientChanged,
    Deleted
}

internal sealed class ArchiveClientBeforeVehicleLockInterceptor(
    ClientsDatabaseFixture fixture,
    Guid clientId) : DbCommandInterceptor
{
    private int _hasArchived;

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal)
            && Interlocked.Exchange(ref this._hasArchived, 1) == 0)
        {
            await using var context = fixture.CreateContext();
            var client = await context.OrganizationClients.SingleAsync(item => item.Id == clientId, cancellationToken);
            client.Delete(DateTime.UtcNow);
            await context.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}

internal sealed class ChangeVehicleBeforeLockInterceptor(
    ClientsDatabaseFixture fixture,
    Guid vehicleId,
    Guid foreignOrganizationId,
    Guid otherClientId,
    VehicleMutation mutation) : DbCommandInterceptor
{
    private int _hasChanged;

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (!command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal)
            || Interlocked.Exchange(ref this._hasChanged, 1) != 0)
        {
            return result;
        }

        await using var context = fixture.CreateContext();
        var vehicle = await context.Vehicles.SingleAsync(item => item.Id == vehicleId, cancellationToken);
        switch (mutation)
        {
            case VehicleMutation.OrganizationNull:
                vehicle.OrganizationId = null;
                break;
            case VehicleMutation.OrganizationChanged:
                vehicle.OrganizationId = foreignOrganizationId;
                break;
            case VehicleMutation.ClientNull:
                vehicle.OrganizationClientId = null;
                break;
            case VehicleMutation.ClientChanged:
                vehicle.OrganizationClientId = otherClientId;
                break;
            case VehicleMutation.Deleted:
                vehicle.IsDeleted = true;
                vehicle.DeletionDate = DateTime.UtcNow;
                break;
        }

        await context.SaveChangesAsync(cancellationToken);

        return result;
    }
}
