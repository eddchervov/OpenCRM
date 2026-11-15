using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Infrastructure.Data;
using System.IO.Compression;
using System.Text.Json;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Organizations.Export;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Services.Interfaces.FileStorages;

namespace OpenCRM.Modules.Organizations.Tests.Integration;

[Collection(OrganizationsDatabaseCollection.Name)]
public sealed class OrganizationDataExportTests(OrganizationsDatabaseFixture fixture) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await using var context = fixture.CreateContext();
        await context.Set<OrganizationDataExportJob>().ExecuteDeleteAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ExportPreservesRelationsAndFilesWithoutOtherOrganizationsOrSecrets()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context, "Детейлинг Ёж");
        organization.WorkingDays = [1, 3, 5];
        var other = await fixture.CreateOrganizationAsync(context, "Чужая организация");
        var client = new OrganizationClient
        {
            OrganizationId = organization.Id,
            Name = "Иванов Семён Петрович",
            LastName = "Иванов",
            FirstName = "Семён",
            MiddleName = "Петрович",
            MaxLinkTokenHash = "secret-max-token",
            TelegramLinkTokenHash = "secret-telegram-token"
        };

        var vehicle = new Vehicle
        {
            OrganizationId = organization.Id,
            UserId = organization.OwnerUserId,
            OrganizationClient = client,
            Name = "Автомобиль клиента",
            IsArchived = true
        };

        var order = new Domain.Maintenance
        {
            Vehicle = vehicle,
            UserId = organization.OwnerUserId,
            Comment = "История работы",
            Details = [new MaintenanceDetail { Name = "Мойка", Price = 100 }, new MaintenanceDetail { Name = "Полировка", Price = 200 }]
        };

        var photo = new VehiclePhoto
        {
            Id = Guid.NewGuid(), Vehicle = vehicle, Path = "shared-storage/photo",
            OriginalFileName = "фото.jpg", ContentType = "image/jpeg"
        };

        var missing = new MaintenancePhoto
        {
            Id = Guid.NewGuid(), Maintenance = order, Vehicle = vehicle, Path = "missing-photo",
            OriginalFileName = "missing.jpg", ContentType = "image/jpeg"
        };

        var document = new MaintenanceDocument
        {
            Maintenance = order, VehicleId = vehicle.Id, OrganizationId = organization.Id,
            UserId = organization.OwnerUserId, CreatedByUserId = organization.OwnerUserId,
            Kind = "work-act", Format = "pdf", Title = "Акт", FileName = "акт.pdf",
            ContentType = "application/pdf", StoragePath = "shared-storage/document", Version = 1
        };

        context.Vehicles.AddRange(vehicle,
            new Vehicle { UserId = organization.OwnerUserId, Name = "Личная техника" },
            new Vehicle { OrganizationId = other.Id, UserId = organization.OwnerUserId, Name = "Чужая техника" });
        context.Maintenance.Add(order);
        context.VehiclePhotos.Add(photo);
        context.MaintenancePhotos.Add(missing);
        context.MaintenanceDocuments.Add(document);
        await context.SaveChangesAsync();

        var storage = new Mock<IFileStorageService>(MockBehavior.Strict);
        storage.Setup(value => value.GetFileAsync(missing.Path, It.IsAny<CancellationToken>())).ThrowsAsync(new FileNotFoundException());
        storage.Setup(value => value.GetFileAsync(photo.Path, It.IsAny<CancellationToken>())).ReturnsAsync(() => new MemoryStream([1, 2, 3]));
        storage.Setup(value => value.GetFileAsync(document.StoragePath, It.IsAny<CancellationToken>())).ReturnsAsync(() => new MemoryStream([4, 5, 6]));
        var builder = CreateBuilder(context, storage.Object);

        await using var stream = await builder.CreateAsync(organization.Id, CancellationToken.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        using var manifest = await ReadJsonAsync(archive, "manifest.json");
        using var vehicles = await ReadJsonAsync(archive, "data/vehicles.json");
        using var orders = await ReadJsonAsync(archive, "data/orders.json");
        using var lines = await ReadJsonAsync(archive, "data/order-lines.json");
        using var clients = await ReadJsonAsync(archive, "data/clients.json");
        using var organizationData = await ReadJsonAsync(archive, "data/organization.json");
        var exportedVehicle = Assert.Single(vehicles.RootElement.EnumerateArray());
        var exportedOrder = Assert.Single(orders.RootElement.EnumerateArray());
        var exportedClient = Assert.Single(clients.RootElement.EnumerateArray());

        Assert.Equal(vehicle.Id, exportedVehicle.GetProperty("id").GetGuid());
        Assert.True(exportedVehicle.GetProperty("isArchived").GetBoolean());
        Assert.Equal(vehicle.Id, exportedOrder.GetProperty("vehicleId").GetGuid());
        Assert.Equal(client.Id, exportedVehicle.GetProperty("organizationClientId").GetGuid());
        Assert.Equal(["Мойка", "Полировка"], lines.RootElement.EnumerateArray()
            .OrderBy(value => value.GetProperty("position").GetInt32())
            .Select(value => value.GetProperty("name").GetString()));
        Assert.Equal("Иванов Семён Петрович", exportedClient.GetProperty("name").GetString());
        Assert.Equal("Иванов", exportedClient.GetProperty("lastName").GetString());
        Assert.Equal("Семён", exportedClient.GetProperty("firstName").GetString());
        Assert.Equal("Петрович", exportedClient.GetProperty("middleName").GetString());
        Assert.Equal(JsonValueKind.Null, exportedClient.GetProperty("companyTypeCode").ValueKind);
        Assert.Equal([1, 3, 5], Assert.Single(organizationData.RootElement.EnumerateArray())
            .GetProperty("workingDays").EnumerateArray().Select(value => value.GetInt32()));
        Assert.DoesNotContain("secret", exportedClient.GetRawText());
        Assert.False(exportedClient.TryGetProperty("maxLinkTokenHash", out _));
        Assert.False(exportedClient.TryGetProperty("telegramLinkTokenHash", out _));
        Assert.Equal(1, manifest.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal($"files/order-photos/{missing.Id:N}.jpg",
            Assert.Single(manifest.RootElement.GetProperty("missingFiles").EnumerateArray()).GetString());
        Assert.NotNull(archive.GetEntry($"files/vehicle-photos/{photo.Id:N}.jpg"));
        Assert.NotNull(archive.GetEntry($"files/documents/{document.Id:N}.pdf"));
        Assert.DoesNotContain(archive.Entries, value => value.FullName.Contains("account", StringComparison.OrdinalIgnoreCase));
        storage.VerifyAll();
    }

    [Fact]
    public async Task ExportUsesMimeTypeThenOriginalNameThenStoragePathForExtensions()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var vehicle = new Vehicle { OrganizationId = organization.Id, UserId = organization.OwnerUserId };
        var photos = new[]
        {
            new VehiclePhoto { OriginalFileName = "фото", ContentType = "image/png; charset=binary", Path = "photo-1" },
            new VehiclePhoto { OriginalFileName = "фото.JPEG", ContentType = "application/octet-stream", Path = "photo-2" },
            new VehiclePhoto { OriginalFileName = "фото", ContentType = "application/octet-stream", Path = "stored/photo-3.WEBP" },
            new VehiclePhoto { OriginalFileName = "фото", ContentType = "application/octet-stream", Path = "photo-4" },
            new VehiclePhoto { OriginalFileName = "фото.bad/name", ContentType = "application/octet-stream", Path = "photo-5" }
        };

        foreach (var photo in photos)
        {
            photo.Id = Guid.NewGuid();
            photo.Vehicle = vehicle;
        }

        context.VehiclePhotos.AddRange(photos);
        await context.SaveChangesAsync();
        var storage = new Mock<IFileStorageService>();
        storage.Setup(value => value.GetFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream([1, 2, 3]));
        var builder = CreateBuilder(context, storage.Object);

        await using var stream = await builder.CreateAsync(organization.Id, CancellationToken.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var extensions = new[] { ".png", ".jpeg", ".webp", ".bin", ".bin" };
        for (var i = 0; i < photos.Length; i++)
        {
            Assert.NotNull(archive.GetEntry($"files/vehicle-photos/{photos[i].Id:N}{extensions[i]}"));
        }
    }

    [Fact]
    public async Task ArchivedOrganizationWithoutSubscriptionCanBeExportedAndTemporaryFileIsRemoved()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        organization.Archive(DateTime.UtcNow);
        await context.SaveChangesAsync();
        var builder = CreateBuilder(context, Mock.Of<IFileStorageService>());
        var stream = (FileStream)await builder.CreateAsync(organization.Id, CancellationToken.None);
        var path = stream.Name;

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            using var clients = await ReadJsonAsync(archive, "data/clients.json");
            Assert.Empty(clients.RootElement.EnumerateArray());
        }

        await stream.DisposeAsync();

        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task FailedBuildDoesNotConsumeDailyRetryWindow()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var options = CreateOptions(retryDelaySeconds: 1);
        var storage = CreateStorage();
        storage.Setup(value => value.SavePrivateFileAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Хранилище недоступно."));
        storage.Setup(value => value.DeletePrivateFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var service = CreateService(context, storage.Object, options);
        var requested = await service.RequestAsync(organization.OwnerUserId, organization.Id);

        await service.ProcessPendingAsync(CancellationToken.None);

        var failed = await service.GetStatusAsync(organization.OwnerUserId, organization.Id);
        Assert.Equal("failed", failed.State);
        await context.Set<OrganizationDataExportJob>().Where(job => job.Id == requested.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(job => job.FinishedAtUtc, DateTime.UtcNow.AddMinutes(-1)));

        var retried = await service.RequestAsync(organization.OwnerUserId, organization.Id);

        Assert.Equal("queued", retried.State);
        Assert.NotEqual(requested.Id, retried.Id);
    }

    [Fact]
    public async Task RepeatedRequestReusesSameReadyArchive()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var stored = new Dictionary<string, byte[]>();
        var storage = CreateStorage();
        storage.Setup(value => value.SavePrivateFileAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns<string, Stream, CancellationToken>(async (path, source, cancellationToken) =>
            {
                using var copy = new MemoryStream();
                await source.CopyToAsync(copy, cancellationToken);
                stored[path] = copy.ToArray();

                return path;
            });
        storage.Setup(value => value.GetPrivateFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((path, _) => Task.FromResult<Stream>(new MemoryStream(stored[path])));
        var service = CreateService(context, storage.Object, CreateOptions());
        var queued = await service.RequestAsync(organization.OwnerUserId, organization.Id);
        await service.ProcessPendingAsync(CancellationToken.None);
        var ready = await service.GetStatusAsync(organization.OwnerUserId, organization.Id);
        var repeated = await service.RequestAsync(organization.OwnerUserId, organization.Id);

        Assert.Equal(queued.Id, ready.Id);
        Assert.Equal("ready", ready.State);
        Assert.Equal(ready.Id, repeated.Id);
        Assert.Equal("ready", repeated.State);
        Assert.Single(stored);

        await using var first = await service.OpenAsync(organization.OwnerUserId, organization.Id, ready.Id!.Value);
        using var firstCopy = new MemoryStream();
        await first.CopyToAsync(firstCopy);
        await using var second = await service.OpenAsync(organization.OwnerUserId, organization.Id, ready.Id.Value);
        using var secondCopy = new MemoryStream();
        await second.CopyToAsync(secondCopy);

        Assert.Equal(firstCopy.ToArray(), secondCopy.ToArray());
        Assert.NotEmpty(firstCopy.ToArray());
    }

    [Fact]
    public async Task OnlyOwnerCanRequestReadAndDownloadExport()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var employee = new ApplicationUser { Email = $"export-employee-{Guid.NewGuid():N}@example.test" };
        await fixture.AddMemberAsync(context, organization, employee);
        var service = CreateService(context, Mock.Of<IFileStorageService>(), CreateOptions());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RequestAsync(employee.Id, organization.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetStatusAsync(employee.Id, organization.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.OpenAsync(employee.Id, organization.Id, Guid.NewGuid()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RequestAsync(organization.OwnerUserId, Guid.NewGuid()));
    }

    [Fact]
    public async Task UnconfiguredPrivateStorageDoesNotQueueOrConsumeExportQuota()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var storage = new Mock<IFileStorageService>(MockBehavior.Strict);
        storage.Setup(value => value.ValidatePrivateStorage()).Throws(new InvalidOperationException("Private storage is not configured."));
        var service = CreateService(context, storage.Object, CreateOptions());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RequestAsync(organization.OwnerUserId, organization.Id));
        Assert.Empty(await context.Set<OrganizationDataExportJob>().AsNoTracking().ToListAsync());

        storage.Setup(value => value.ValidatePrivateStorage());
        var queued = await service.RequestAsync(organization.OwnerUserId, organization.Id);

        Assert.Equal("queued", queued.State);
        Assert.Single(await context.Set<OrganizationDataExportJob>().AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task QueueLimitIsEnforcedAcrossConcurrentOrganizations()
    {
        await using var context = fixture.CreateContext();
        var firstOrganization = await fixture.CreateOrganizationAsync(context);
        var secondOrganization = await fixture.CreateOrganizationAsync(context);
        var options = CreateOptions(queueLimit: 1);

        async Task<bool> TryQueueAsync(Guid organizationId, string ownerId)
        {
            await using var requestContext = fixture.CreateContext();
            var service = CreateService(requestContext, Mock.Of<IFileStorageService>(), options);
            try
            {
                var result = await service.RequestAsync(ownerId, organizationId);

                return result.State == "queued";
            }
            catch (OrganizationDataExportResourceLimitException)
            {
                return false;
            }
        }

        var results = await Task.WhenAll(
            TryQueueAsync(firstOrganization.Id, firstOrganization.OwnerUserId),
            TryQueueAsync(secondOrganization.Id, secondOrganization.OwnerUserId));

        Assert.Single(results, value => value);
        Assert.Single(results, value => !value);
        Assert.Equal(1, await context.Set<OrganizationDataExportJob>().CountAsync(job => job.State == "queued"));
    }

    [Fact]
    public async Task DownloadSlotsAreReleasedWhenResponseStreamIsDisposed()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var job = new OrganizationDataExportJob
        {
            OrganizationId = organization.Id,
            RequestedByUserId = organization.OwnerUserId,
            State = "ready",

            CompletedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(24)
        };
        job.StoragePath = $"organization-exports/{organization.Id:N}/{job.Id:N}.zip";
        context.Add(job);
        await context.SaveChangesAsync();
        var storage = CreateStorage();
        storage.Setup(value => value.GetPrivateFileAsync(job.StoragePath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream([1, 2, 3]));
        var service = CreateService(context, storage.Object, CreateOptions());
        await using var first = await service.OpenAsync(organization.OwnerUserId, organization.Id, job.Id);
        await using var second = await service.OpenAsync(organization.OwnerUserId, organization.Id, job.Id);

        await Assert.ThrowsAsync<OrganizationDataExportResourceLimitException>(
            () => service.OpenAsync(organization.OwnerUserId, organization.Id, job.Id));
        storage.Verify(value => value.GetPrivateFileAsync(job.StoragePath, It.IsAny<CancellationToken>()), Times.Exactly(2));

        await first.DisposeAsync();
        await using var third = await service.OpenAsync(organization.OwnerUserId, organization.Id, job.Id);
        storage.Verify(value => value.GetPrivateFileAsync(job.StoragePath, It.IsAny<CancellationToken>()), Times.Exactly(3));

        await second.DisposeAsync();
    }

    [Fact]
    public async Task WorkerMarksStaleRunningJobFailedBeforeProcessingQueue()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var job = new OrganizationDataExportJob
        {
            OrganizationId = organization.Id,
            RequestedByUserId = organization.OwnerUserId,
            State = "running",
            StartedAtUtc = DateTime.UtcNow.AddHours(-1)
        };
        context.Add(job);
        await context.SaveChangesAsync();
        var service = CreateService(context, Mock.Of<IFileStorageService>(), CreateOptions());

        await service.ProcessPendingAsync(CancellationToken.None);

        var stale = await context.Set<OrganizationDataExportJob>().AsNoTracking().SingleAsync(value => value.Id == job.Id);
        Assert.Equal("failed", stale.State);
        Assert.NotNull(stale.FinishedAtUtc);
        Assert.Contains("прервана", stale.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<JsonDocument> ReadJsonAsync(ZipArchive archive, string name)
    {
        await using var stream = archive.GetEntry(name)!.Open();

        return await JsonDocument.ParseAsync(stream);
    }

    private static OrganizationDataArchiveBuilder CreateBuilder(OpenCRMContext context, IFileStorageService storage) =>
        new(context, storage, CreateOptions());

    private static OrganizationDataExportService CreateService(
        OpenCRMContext context,
        IFileStorageService storage,
        OrganizationDataExportOptions options) =>
        new(context, storage, options, Microsoft.Extensions.Logging.Abstractions.NullLogger<OrganizationDataExportService>.Instance);

    private static OrganizationDataExportOptions CreateOptions(int queueLimit = 20, int retryDelaySeconds = 60) => new()
    {
        QueueLimit = queueLimit,
        RetryDelaySeconds = retryDelaySeconds,
        MinimumFreeDiskBytes = 0,
        SpoolDirectory = Path.Combine(Path.GetTempPath(), "opencrm-export-tests")
    };

    private static Mock<IFileStorageService> CreateStorage() => new();
}
