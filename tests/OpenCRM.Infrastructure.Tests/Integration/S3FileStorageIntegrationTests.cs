using Amazon.S3;
using Amazon.S3.Model;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCRM.Infrastructure.FileStorage.FileStorages;
using OpenCRM.Infrastructure.FileStorage.Media;
using OpenCRM.Services.Interfaces.Media.Dto;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OpenCRM.Infrastructure.Tests.Integration;

public sealed class S3FileStorageIntegrationTests : IAsyncLifetime
{
    private const string BucketName = "opencrm-media-tests";

    private readonly IContainer _storage = new ContainerBuilder("adobe/s3mock:latest")
        .WithPortBinding(9090, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(9090))
        .Build();

    [Fact]
    public async Task ImagesAndDocumentsRemainAvailableAtTheirRequestedPaths()
    {
        using var client = new AmazonS3Client("test-access", "test-secret", new AmazonS3Config
        {
            ServiceURL = $"http://{this._storage.Hostname}:{this._storage.GetMappedPublicPort(9090)}",
            ForcePathStyle = true,
            UseHttp = true
        });

        await client.PutBucketAsync(new PutBucketRequest { BucketName = BucketName });

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["S3:ServiceUrl"] = client.Config.ServiceURL,
            ["S3:BucketName"] = BucketName
        }).Build();

        using var storage = new S3FileStorageProvider(configuration, NullLogger<S3FileStorageProvider>.Instance, client);
        var processor = new ImageProcessingService(storage, NullLogger<ImageProcessingService>.Instance);
        using var sourceImage = new Image<Rgba32>(4, 4);
        using var source = new MemoryStream();
        await sourceImage.SaveAsPngAsync(source);
        var firstPath = await this.SaveImageAsync(processor, source, "organizations/one/photos");
        var otherFolderPath = await this.SaveImageAsync(processor, source, "organizations/one/albums/album");
        var otherOrganizationPath = await this.SaveImageAsync(processor, source, "organizations/two/photos");
        var repeatedPath = await this.SaveImageAsync(processor, source, "organizations/one/photos");

        Assert.Equal(firstPath, repeatedPath);
        Assert.Equal(3, new[] { firstPath, otherFolderPath, otherOrganizationPath }.Distinct().Count());
        foreach (var path in new[] { firstPath, otherFolderPath, otherOrganizationPath })
        {
            Assert.True(await storage.FileExistsAsync(path));
            using var content = await storage.GetFileAsync(path);
            using var decoded = await Image.LoadAsync(content);
            Assert.Equal(4, decoded.Width);
            Assert.Equal(4, decoded.Height);
        }

        using var firstDocument = new MemoryStream([1, 2, 3]);
        using var secondDocument = new MemoryStream([1, 2, 3]);
        var documentPaths = await Task.WhenAll(
            storage.SaveFileAsync("organizations/one/document.pdf", firstDocument),
            storage.SaveFileAsync("organizations/two/receipt.pdf", secondDocument));

        Assert.Equal(["organizations/one/document.pdf", "organizations/two/receipt.pdf"], documentPaths);
        foreach (var path in documentPaths)
        {
            using var response = await client.GetObjectAsync(BucketName, path);
            using var content = new MemoryStream();
            await response.ResponseStream.CopyToAsync(content);
            Assert.Equal(new byte[] { 1, 2, 3 }, content.ToArray());
        }

        Assert.False(await storage.FileExistsAsync("missing.jpg"));
        Assert.True(await storage.DeleteFileAsync(otherFolderPath));
        Assert.False(await storage.FileExistsAsync(otherFolderPath));
        Assert.True(await storage.FileExistsAsync(firstPath));
        Assert.True(await storage.FileExistsAsync(otherOrganizationPath));
    }

    [Fact]
    public async Task PrivateObjectsUseSeparateBucketAndCleanupDoesNotDeleteMedia()
    {
        using var client = new AmazonS3Client("test-access", "test-secret", new AmazonS3Config
        {
            ServiceURL = $"http://{this._storage.Hostname}:{this._storage.GetMappedPublicPort(9090)}",
            ForcePathStyle = true, UseHttp = true
        });
        await client.PutBucketAsync(new PutBucketRequest { BucketName = BucketName });
        await client.PutBucketAsync(new PutBucketRequest { BucketName = "opencrm-private-tests" });
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["S3:ServiceUrl"] = client.Config.ServiceURL, ["S3:BucketName"] = BucketName,
            ["S3:PrivateBucketName"] = "opencrm-private-tests"
        }).Build();
        using var storage = new S3FileStorageProvider(configuration, NullLogger<S3FileStorageProvider>.Instance, client);
        using var media = new MemoryStream([1, 2, 3]);
        using var archive = new MemoryStream([7, 8, 9]);
        const string path = "qa/shared-name.zip";
        await storage.SaveFileAsync(path, media);
        await storage.SavePrivateFileAsync(path, archive, CancellationToken.None);
        await using (var downloaded = await storage.GetPrivateFileAsync(path, CancellationToken.None))
        {
            using var copy = new MemoryStream();
            await downloaded.CopyToAsync(copy);
            Assert.Equal(new byte[] { 7, 8, 9 }, copy.ToArray());
        }

        Assert.True(await storage.DeletePrivateFileAsync(path, CancellationToken.None));
        await Assert.ThrowsAsync<FileNotFoundException>(() => storage.GetPrivateFileAsync(path, CancellationToken.None));
        Assert.True(await storage.FileExistsAsync(path));
    }

    public Task InitializeAsync() => this._storage.StartAsync();

    public Task DisposeAsync() => this._storage.DisposeAsync().AsTask();

    private async Task<string> SaveImageAsync(ImageProcessingService processor, Stream source, string folder)
    {
        source.Position = 0;

        return await processor.ProcessAndSaveImageAsync(source, new ImageProcessingOptions
        {
            TargetFolder = folder,
            MaxWidth = 4,
            MaxHeight = 4,
            Quality = 75
        });
    }
}
