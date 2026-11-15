using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenCRM.Infrastructure.FileStorage.FileStorages;
using OpenCRM.Infrastructure.FileStorage.Media;
using OpenCRM.Services.Interfaces.FileStorages;
using OpenCRM.Services.Interfaces.Media.Dto;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace OpenCRM.Infrastructure.Tests.Unit;

public sealed class S3FileStorageWriteTests
{
    public static TheoryData<string, byte[]> SupportedImageData => new()
    {
        { "JPEG", CreateImageData(new JpegEncoder()) },
        { "PNG", CreateImageData(new PngEncoder()) },
        { "GIF", CreateImageData(new GifEncoder()) },
        { "BMP", CreateImageData(new BmpEncoder()) },
        { "WebP", CreateImageData(new WebpEncoder()) }
    };

    [Fact]
    public async Task TiffImageDoesNotReachStorage()
    {
        var storage = new Mock<IFileStorageService>(MockBehavior.Strict);
        var service = new ImageProcessingService(storage.Object, NullLogger<ImageProcessingService>.Instance);
        using var image = new MemoryStream(CreateImageData(new TiffEncoder()));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.ProcessAndSaveImageAsync(image, new ImageProcessingOptions { TargetFolder = "audit" }));

        Assert.Contains("формат", exception.Message);
        storage.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(SupportedImageData))]
    public async Task SupportedImageIsProcessedAndWrittenToStorage(string _, byte[] imageData)
    {
        var storage = new Mock<IFileStorageService>(MockBehavior.Strict);
        storage.Setup(value => value.SaveFileAsync(
                It.Is<string>(path => path.StartsWith("audit/", StringComparison.Ordinal) && path.EndsWith(".jpg", StringComparison.Ordinal)),
                It.Is<Stream>(stream => stream.Length > 0)))
            .ReturnsAsync("actual/returned.jpg");
        var service = new ImageProcessingService(storage.Object, NullLogger<ImageProcessingService>.Instance);
        using var image = new MemoryStream(imageData);

        var path = await service.ProcessAndSaveImageAsync(image, new ImageProcessingOptions
        {
            TargetFolder = "audit",
            MaxWidth = 1200,
            MaxHeight = 800,
            Quality = 75,
            GenerateHash = false
        });

        Assert.Equal("actual/returned.jpg", path);
        storage.Verify(value => value.SaveFileAsync(
            It.Is<string>(target => target.StartsWith("audit/", StringComparison.Ordinal)),
            It.IsAny<Stream>()), Times.Once);
        storage.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("bm90LWFuLWltYWdl")]
    [InlineData("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aE1sAAAAASUVORK5CYII=")]
    public async Task InvalidImageDoesNotReachStorage(string encodedImage)
    {
        var storage = new Mock<IFileStorageService>(MockBehavior.Strict);
        var service = new ImageProcessingService(storage.Object, NullLogger<ImageProcessingService>.Instance);
        using var image = new MemoryStream(Convert.FromBase64String(encodedImage));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.ProcessAndSaveImageAsync(image, new ImageProcessingOptions { TargetFolder = "audit" }));

        Assert.Contains("повреждено", exception.Message);
        storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SaveWritesToRequestedPathWithoutListingBucket()
    {
        var client = new Mock<IAmazonS3>();
        client.Setup(value => value.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PutObjectResponse { HttpStatusCode = HttpStatusCode.OK });
        using var storage = CreateStorage(client.Object);
        using var firstContent = new MemoryStream([1, 2, 3]);
        using var secondContent = new MemoryStream([1, 2, 3]);
        using var thirdContent = new MemoryStream([4, 5, 6]);

        var results = await Task.WhenAll(
            storage.SaveFileAsync("organizations/one/image.jpg", firstContent),
            storage.SaveFileAsync("organizations/two/image.jpg", secondContent),
            storage.SaveFileAsync("organizations/one/other.jpg", thirdContent));

        Assert.Equal(
            ["organizations/one/image.jpg", "organizations/two/image.jpg", "organizations/one/other.jpg"],
            results);
        client.Verify(value => value.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()), Times.Never);
        client.Verify(value => value.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        client.Verify(value => value.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task FileExistsPropagatesStorageFailures(HttpStatusCode statusCode)
    {
        var error = new AmazonS3Exception("S3 недоступен.") { StatusCode = statusCode };
        var client = new Mock<IAmazonS3>();
        client.Setup(value => value.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(error);
        using var storage = CreateStorage(client.Object);

        var result = await Assert.ThrowsAsync<AmazonS3Exception>(() => storage.FileExistsAsync("image.jpg"));

        Assert.Same(error, result);
    }

    [Fact]
    public async Task ImageProcessingReturnsPathFromStorage()
    {
        var storage = new Mock<IFileStorageService>();
        storage.Setup(value => value.FileExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
        storage.Setup(value => value.SaveFileAsync(It.IsAny<string>(), It.IsAny<Stream>()))
            .ReturnsAsync("actual/returned.jpg");
        var service = new ImageProcessingService(storage.Object, NullLogger<ImageProcessingService>.Instance);
        using var sourceImage = new Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(2, 2);
        using var imageStream = new MemoryStream();
        await sourceImage.SaveAsJpegAsync(imageStream);
        imageStream.Position = 0;

        var path = await service.ProcessAndSaveImageAsync(imageStream, new ImageProcessingOptions
        {
            TargetFolder = "requested/folder",
            MaxWidth = 1200,
            MaxHeight = 800,
            Quality = 75,
            GenerateHash = false
        });

        Assert.Equal("actual/returned.jpg", path);
    }

    private static byte[] CreateImageData(SixLabors.ImageSharp.Formats.IImageEncoder encoder)
    {
        using var sourceImage = new Image<Rgba32>(2, 2);
        using var imageStream = new MemoryStream();
        sourceImage.Save(imageStream, encoder);

        return imageStream.ToArray();
    }

    [Fact]
    public async Task PrivateArchiveUsesSeparateBucketWithoutReadingOrHashingInputBeforeUpload()
    {
        using var cancellation = new CancellationTokenSource();
        var content = new Mock<Stream>(MockBehavior.Strict);
        var client = new Mock<IAmazonS3>(MockBehavior.Strict);
        client.Setup(value => value.PutObjectAsync(It.Is<PutObjectRequest>(request =>
                request.BucketName == "private-test" && request.Key == "exports/job.zip"
                && request.InputStream == content.Object && !request.AutoCloseStream
                && request.ContentType == "application/zip" && request.Metadata.Count == 0), cancellation.Token))
            .ReturnsAsync(new PutObjectResponse { HttpStatusCode = HttpStatusCode.OK });
        using var storage = CreateStorage(client.Object, "private-test");

        var result = await storage.SavePrivateFileAsync("exports/job.zip", content.Object, cancellation.Token);

        Assert.Equal("exports/job.zip", result);
        client.VerifyAll();
        content.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("test")]
    public async Task PrivateArchiveNeverFallsBackToOrdinaryBucket(string? privateBucket)
    {
        var client = new Mock<IAmazonS3>(MockBehavior.Strict);
        using var storage = CreateStorage(client.Object, privateBucket);
        using var content = new MemoryStream([1, 2, 3]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            storage.SavePrivateFileAsync("exports/job.zip", content, CancellationToken.None));

        client.VerifyNoOtherCalls();
    }

    private static S3FileStorageProvider CreateStorage(IAmazonS3 client, string? privateBucket = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["S3:ServiceUrl"] = "https://storage.example.test",
            ["S3:BucketName"] = "test",
            ["S3:PrivateBucketName"] = privateBucket
        }).Build();

        return new S3FileStorageProvider(configuration, NullLogger<S3FileStorageProvider>.Instance, client);
    }
}
