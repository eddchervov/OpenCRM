using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Organizations.Export;
using OpenCRM.Services.Interfaces.FileStorages;
using Moq;

namespace OpenCRM.Modules.Organizations.Tests.Integration;

[Collection(OrganizationsDatabaseCollection.Name)]
public sealed class OrganizationDataArchiveBuilderTests(OrganizationsDatabaseFixture fixture)
{
    [Fact]
    public async Task ArchiveSizeLimitStopsWritingAndRemovesTemporaryFiles()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var spoolDirectory = Path.Combine(Path.GetTempPath(), $"opencrm-export-test-{Guid.NewGuid():N}");
        var options = new OrganizationDataExportOptions
        {
            MaxArchiveBytes = 1,
            MinimumFreeDiskBytes = 0,
            SpoolDirectory = spoolDirectory
        };
        var builder = new OrganizationDataArchiveBuilder(context, Mock.Of<IFileStorageService>(), options);

        try
        {
            await Assert.ThrowsAsync<OrganizationDataExportResourceLimitException>(
                () => builder.CreateAsync(organization.Id, CancellationToken.None));

            Assert.Empty(Directory.GetFiles(spoolDirectory));
        }
        finally
        {
            if (Directory.Exists(spoolDirectory))
            {
                Directory.Delete(spoolDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RowLimitCountsRowsAcrossAllDatasetsAndRemovesTemporaryFiles()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        context.OrganizationClients.AddRange(
            new OrganizationClient { OrganizationId = organization.Id, Name = "QA клиент 1" },
            new OrganizationClient { OrganizationId = organization.Id, Name = "QA клиент 2" });
        await context.SaveChangesAsync();

        var spoolDirectory = Path.Combine(Path.GetTempPath(), $"opencrm-export-test-{Guid.NewGuid():N}");
        var options = new OrganizationDataExportOptions
        {
            MaxRows = 1,
            MinimumFreeDiskBytes = 0,
            SpoolDirectory = spoolDirectory
        };
        var builder = new OrganizationDataArchiveBuilder(context, Mock.Of<IFileStorageService>(), options);

        try
        {
            await Assert.ThrowsAsync<OrganizationDataExportResourceLimitException>(
                () => builder.CreateAsync(organization.Id, CancellationToken.None));

            Assert.Empty(Directory.GetFiles(spoolDirectory));
        }
        finally
        {
            if (Directory.Exists(spoolDirectory))
            {
                Directory.Delete(spoolDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task LargeAttachmentIsCopiedInBoundedChunksIntoDiskArchive()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var vehicle = new Vehicle { OrganizationId = organization.Id, UserId = organization.OwnerUserId };
        var photo = new VehiclePhoto
        {
            Id = Guid.NewGuid(), Vehicle = vehicle, Path = "large-stream", OriginalFileName = "qa.jpg"
        };
        context.VehiclePhotos.Add(photo);
        await context.SaveChangesAsync();
        var directory = Path.Combine(Path.GetTempPath(), $"opencrm-export-test-{Guid.NewGuid():N}");
        var source = new GeneratedReadStream(32L * 1024 * 1024);
        var storage = new Mock<IFileStorageService>(MockBehavior.Strict);
        storage.Setup(value => value.GetFileAsync(photo.Path, It.IsAny<CancellationToken>())).ReturnsAsync(source);
        var options = new OrganizationDataExportOptions { SpoolDirectory = directory, MinimumFreeDiskBytes = 0 };
        try
        {
            await using (var archive = await new OrganizationDataArchiveBuilder(context, storage.Object, options)
                .CreateAsync(organization.Id, CancellationToken.None))
            {
                Assert.IsType<FileStream>(archive);
                Assert.True(archive.Length > 32L * 1024 * 1024);
                Assert.Equal(32L * 1024 * 1024, source.BytesRead);
                Assert.InRange(source.LargestReadRequest, 1, 128 * 1024);
                Assert.True(source.IsDisposed);
                using var zip = new System.IO.Compression.ZipArchive(archive, System.IO.Compression.ZipArchiveMode.Read, true);
                Assert.Equal(source.BytesRead, zip.GetEntry($"files/vehicle-photos/{photo.Id:N}.jpg")!.Length);
            }
            Assert.Empty(Directory.GetFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task SourceByteLimitStopsEvenWhenAttachmentLengthIsUnknown()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var photo = new VehiclePhoto
        {
            Id = Guid.NewGuid(),
            Vehicle = new Vehicle { OrganizationId = organization.Id, UserId = organization.OwnerUserId },
            Path = "unknown-length", OriginalFileName = "qa.jpg"
        };
        context.VehiclePhotos.Add(photo);
        await context.SaveChangesAsync();
        var directory = Path.Combine(Path.GetTempPath(), $"opencrm-export-test-{Guid.NewGuid():N}");
        var source = new GeneratedReadStream(32L * 1024 * 1024);
        var storage = new Mock<IFileStorageService>(MockBehavior.Strict);
        storage.Setup(value => value.GetFileAsync(photo.Path, It.IsAny<CancellationToken>())).ReturnsAsync(source);
        var options = new OrganizationDataExportOptions
        {
            SpoolDirectory = directory, MinimumFreeDiskBytes = 0, MaxSourceBytes = 1024 * 1024
        };
        try
        {
            await Assert.ThrowsAsync<OrganizationDataExportResourceLimitException>(() =>
                new OrganizationDataArchiveBuilder(context, storage.Object, options).CreateAsync(organization.Id, CancellationToken.None));
            Assert.InRange(source.BytesRead, 1, options.MaxSourceBytes + 128 * 1024);
            Assert.True(source.IsDisposed);
            Assert.Empty(Directory.GetFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task OversizedDescriptionIsRejectedAndTemporaryFilesAreRemoved()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context, "QA");
        context.MaintenanceHistoryEvents.Add(CreateHistoryEvent(organization, description: new string('x', 101)));
        await context.SaveChangesAsync();
        var directory = Path.Combine(Path.GetTempPath(), $"opencrm-export-test-{Guid.NewGuid():N}");
        var options = CreateSmallFieldOptions(directory);

        try
        {
            await Assert.ThrowsAsync<OrganizationDataExportResourceLimitException>(() =>
                new OrganizationDataArchiveBuilder(context, Mock.Of<IFileStorageService>(), options)
                    .CreateAsync(organization.Id, CancellationToken.None));

            Assert.Empty(Directory.GetFiles(directory));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task OversizedMaintenanceHistoryJsonIsRejectedAndTemporaryFilesAreRemoved()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context, "QA");
        context.MaintenanceHistoryEvents.Add(CreateHistoryEvent(organization,
            payloadJson: $"{{\"payload\":\"{new string('x', 100)}\"}}"));
        await context.SaveChangesAsync();
        var directory = Path.Combine(Path.GetTempPath(), $"opencrm-export-test-{Guid.NewGuid():N}");
        var options = CreateSmallFieldOptions(directory);

        try
        {
            await Assert.ThrowsAsync<OrganizationDataExportResourceLimitException>(() =>
                new OrganizationDataArchiveBuilder(context, Mock.Of<IFileStorageService>(), options)
                    .CreateAsync(organization.Id, CancellationToken.None));

            Assert.Empty(Directory.GetFiles(directory));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task OversizedHistoryInAnotherOrganizationDoesNotBlockArchive()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context, "QA");
        var foreignOrganization = await fixture.CreateOrganizationAsync(context, "Foreign QA");
        context.MaintenanceHistoryEvents.Add(CreateHistoryEvent(foreignOrganization,
            description: new string('x', 101), payloadJson: $"{{\"payload\":\"{new string('x', 100)}\"}}"));
        await context.SaveChangesAsync();
        var directory = Path.Combine(Path.GetTempPath(), $"opencrm-export-test-{Guid.NewGuid():N}");
        var options = CreateSmallFieldOptions(directory);

        try
        {
            await using var archive = await new OrganizationDataArchiveBuilder(context, Mock.Of<IFileStorageService>(), options)
                .CreateAsync(organization.Id, CancellationToken.None);
            using var zip = new System.IO.Compression.ZipArchive(archive, System.IO.Compression.ZipArchiveMode.Read);
            await using var historyStream = zip.GetEntry("data/order-history.json")!.Open();
            using var history = await System.Text.Json.JsonDocument.ParseAsync(historyStream);

            Assert.Empty(history.RootElement.EnumerateArray());
        }
        finally
        {
            Assert.Empty(Directory.GetFiles(directory));
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class GeneratedReadStream(long length) : Stream
    {
        internal long BytesRead { get; private set; }
        internal int LargestReadRequest { get; private set; }
        internal bool IsDisposed { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => this.Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            this.LargestReadRequest = Math.Max(this.LargestReadRequest, buffer.Length);
            var count = (int)Math.Min(buffer.Length, length - this.BytesRead);
            buffer[..count].Fill(42);
            this.BytesRead += count;

            return count;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return ValueTask.FromResult(this.Read(buffer.Span));
        }
        protected override void Dispose(bool disposing)
        {
            this.IsDisposed = true;
            base.Dispose(disposing);
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static OrganizationDataExportOptions CreateSmallFieldOptions(string directory) => new()
    {
        MaxTextCharacters = 100,
        MaxJsonFieldBytes = 64,
        MinimumFreeDiskBytes = 0,
        SpoolDirectory = directory
    };

    private static MaintenanceHistoryEvent CreateHistoryEvent(
        Organization organization,
        string? description = null,
        string? payloadJson = null)
    {
        var vehicle = new Vehicle { OrganizationId = organization.Id, UserId = organization.OwnerUserId };
        var maintenance = new OpenCRM.Domain.Maintenance
        {
            Vehicle = vehicle,
            UserId = organization.OwnerUserId
        };

        return new MaintenanceHistoryEvent
        {
            Maintenance = maintenance,
            MaintenanceId = maintenance.Id,
            VehicleId = vehicle.Id,
            OrganizationId = organization.Id,
            UserId = organization.OwnerUserId,
            EventType = "qa",
            Title = "QA",
            Description = description,
            PayloadJson = payloadJson,
            CreatedAt = DateTime.UtcNow
        };
    }
}
