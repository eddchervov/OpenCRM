using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Organizations.Export;
using OpenCRM.Services.Interfaces.FileStorages;

namespace OpenCRM.Modules.Organizations.Tests.Integration;

[Collection(OrganizationsDatabaseCollection.Name)]
public sealed class OrganizationDataExportResilienceTests(OrganizationsDatabaseFixture fixture) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await using var context = fixture.CreateContext();
        await context.Set<OrganizationDataExportJob>().ExecuteDeleteAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ConcurrentWorkersBuildOneOrganizationAtATimeAcrossIndependentContexts()
    {
        await using var seedContext = fixture.CreateContext();
        var firstOrganization = await fixture.CreateOrganizationAsync(seedContext, "QA export 1");
        var secondOrganization = await fixture.CreateOrganizationAsync(seedContext, "QA export 2");
        var storage = CreateStorage();
        var firstSaveStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstSave = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var saveCount = 0;
        var activeSaves = 0;
        var maximumConcurrentSaves = 0;
        storage.Setup(value => value.SavePrivateFileAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns<string, Stream, CancellationToken>(async (path, source, cancellationToken) =>
            {
                var call = Interlocked.Increment(ref saveCount);
                var active = Interlocked.Increment(ref activeSaves);
                UpdateMaximum(ref maximumConcurrentSaves, active);
                try
                {
                    if (call == 1)
                    {
                        firstSaveStarted.SetResult(true);
                        await releaseFirstSave.Task.WaitAsync(cancellationToken);
                    }

                    using var copy = new MemoryStream();
                    await source.CopyToAsync(copy, cancellationToken);

                    return path;
                }
                finally
                {
                    Interlocked.Decrement(ref activeSaves);
                }
            });

        await using var firstContext = fixture.CreateContext();
        await using var secondContext = fixture.CreateContext();
        var options = CreateOptions();
        var firstWorkerService = CreateService(firstContext, storage.Object, options);
        var secondWorkerService = CreateService(secondContext, storage.Object, options);
        await firstWorkerService.RequestAsync(firstOrganization.OwnerUserId, firstOrganization.Id);
        await firstWorkerService.RequestAsync(secondOrganization.OwnerUserId, secondOrganization.Id);

        var firstWorker = firstWorkerService.ProcessPendingAsync(CancellationToken.None);
        int savesWhileFirstWorkerHeldTheSlot;
        try
        {
            await firstSaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await secondWorkerService.ProcessPendingAsync(CancellationToken.None);
            savesWhileFirstWorkerHeldTheSlot = Volatile.Read(ref saveCount);
        }
        finally
        {
            releaseFirstSave.TrySetResult(true);
        }

        await firstWorker;

        await secondWorkerService.ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(1, savesWhileFirstWorkerHeldTheSlot);
        Assert.Equal(2, Volatile.Read(ref saveCount));
        Assert.Equal(1, Volatile.Read(ref maximumConcurrentSaves));
    }

    [Fact]
    public async Task CleanupDeletesExpiredArtifactsAndOldMetadataButKeepsFreshJobAndBusinessData()
    {
        await using var seedContext = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seedContext, "QA cleanup");
        var client = new OrganizationClient { OrganizationId = organization.Id, Name = "QA customer" };
        seedContext.OrganizationClients.Add(client);
        var now = DateTime.UtcNow;
        var expiredArtifact = new OrganizationDataExportJob
        {
            OrganizationId = organization.Id,
            RequestedByUserId = organization.OwnerUserId,
            State = "ready",
            StoragePath = $"organization-exports/{organization.Id:N}/expired.zip",
            RequestedAtUtc = now.AddDays(-2),
            FinishedAtUtc = now.AddDays(-2),
            CompletedAtUtc = now.AddDays(-2),
            ExpiresAtUtc = now.AddHours(-1),
            CleanupAfterUtc = now.AddHours(-1),
            ArchiveSizeBytes = 120
        };
        var oldFailure = new OrganizationDataExportJob
        {
            OrganizationId = organization.Id,
            RequestedByUserId = organization.OwnerUserId,
            State = "failed",
            RequestedAtUtc = now.AddDays(-40),
            FinishedAtUtc = now.AddDays(-31),
            CleanupAfterUtc = now.AddDays(-31)
        };
        var freshFailure = new OrganizationDataExportJob
        {
            OrganizationId = organization.Id,
            RequestedByUserId = organization.OwnerUserId,
            State = "failed",
            RequestedAtUtc = now.AddDays(-2),
            FinishedAtUtc = now.AddDays(-1),
            CleanupAfterUtc = now.AddDays(29)
        };
        seedContext.AddRange(expiredArtifact, oldFailure, freshFailure);
        await seedContext.SaveChangesAsync();

        var storage = CreateStorage();
        storage.Setup(value => value.DeletePrivateFileAsync(
                expiredArtifact.StoragePath!, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        await using var workerContext = fixture.CreateContext();
        var service = CreateService(workerContext, storage.Object, CreateOptions());

        await service.ProcessPendingAsync(CancellationToken.None);

        var expired = await seedContext.Set<OrganizationDataExportJob>().AsNoTracking()
            .SingleAsync(value => value.Id == expiredArtifact.Id);
        Assert.Equal("expired", expired.State);
        Assert.Null(expired.StoragePath);
        Assert.InRange(expired.CleanupAfterUtc!.Value, DateTime.UtcNow.AddDays(29).AddMinutes(-1), DateTime.UtcNow.AddDays(30).AddMinutes(1));
        Assert.False(await seedContext.Set<OrganizationDataExportJob>().AnyAsync(value => value.Id == oldFailure.Id));
        var fresh = await seedContext.Set<OrganizationDataExportJob>().AsNoTracking()
            .SingleAsync(value => value.Id == freshFailure.Id);
        Assert.Equal("failed", fresh.State);
        Assert.Equal(1, await seedContext.OrganizationClients.CountAsync(value => value.Id == client.Id));
        storage.Verify(value => value.DeletePrivateFileAsync(expiredArtifact.StoragePath!, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OwnerChangeAfterPrivateSaveFailsJobAndDeletesUnpublishedArtifact()
    {
        await using var seedContext = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(seedContext, "QA owner change");
        var replacementOwner = new ApplicationUser { Email = $"export-new-owner-{Guid.NewGuid():N}@example.test" };
        await fixture.AddMemberAsync(seedContext, organization, replacementOwner, OrganizationRoleCodes.Owner);
        var storage = CreateStorage();
        storage.Setup(value => value.SavePrivateFileAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns<string, Stream, CancellationToken>(async (path, source, cancellationToken) =>
            {
                using var copy = new MemoryStream();
                await source.CopyToAsync(copy, cancellationToken);
                await using var ownershipContext = fixture.CreateContext();
                await ownershipContext.Organizations.Where(value => value.Id == organization.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(value => value.OwnerUserId, replacementOwner.Id), cancellationToken);

                return path;
            });
        var deletedPaths = new List<string>();
        storage.Setup(value => value.DeletePrivateFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((path, _) =>
            {
                deletedPaths.Add(path);

                return Task.FromResult(true);
            });
        await using var workerContext = fixture.CreateContext();
        var service = CreateService(workerContext, storage.Object, CreateOptions());
        var queued = await service.RequestAsync(organization.OwnerUserId, organization.Id);

        await service.ProcessPendingAsync(CancellationToken.None);

        var failed = await seedContext.Set<OrganizationDataExportJob>().AsNoTracking()
            .SingleAsync(value => value.Id == queued.Id);
        Assert.Equal("failed", failed.State);
        Assert.Null(failed.CompletedAtUtc);
        Assert.Null(failed.StoragePath);
        Assert.Contains("Доступ владельца изменился", failed.ErrorMessage);
        Assert.Equal([$"organization-exports/{organization.Id:N}/{queued.Id:N}.zip"], deletedPaths);
    }

    private static OrganizationDataExportService CreateService(
        OpenCRMContext context,
        IFileStorageService storage,
        OrganizationDataExportOptions options) =>
        new(context, storage, options, NullLogger<OrganizationDataExportService>.Instance);

    private static OrganizationDataExportOptions CreateOptions() => new()
    {
        MinimumFreeDiskBytes = 0,
        SpoolDirectory = Path.Combine(Path.GetTempPath(), "opencrm-export-resilience-tests")
    };

    private static Mock<IFileStorageService> CreateStorage()
    {
        var storage = new Mock<IFileStorageService>(MockBehavior.Strict);
        storage.Setup(value => value.ValidatePrivateStorage());

        return storage;
    }

    private static void UpdateMaximum(ref int maximum, int value)
    {
        var current = Volatile.Read(ref maximum);
        while (value > current)
        {
            var previous = Interlocked.CompareExchange(ref maximum, value, current);
            if (previous == current)
            {
                return;
            }

            current = previous;
        }
    }
}
