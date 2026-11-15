using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenCRM.Infrastructure.FileStorage.FileStorages;

namespace OpenCRM.Infrastructure.Tests.Unit;

public sealed class S3FileStorageReadTests
{
    [Theory]
    [InlineData("NoSuchKey", HttpStatusCode.BadRequest)]
    [InlineData("NotFound", HttpStatusCode.BadRequest)]
    [InlineData("Other", HttpStatusCode.NotFound)]
    public async Task MissingObjectIsReportedAsFileNotFound(string errorCode, HttpStatusCode statusCode)
    {
        var error = new AmazonS3Exception("Ошибка S3.") { ErrorCode = errorCode, StatusCode = statusCode };
        using var storage = CreateStorage(error);

        var result = await Assert.ThrowsAsync<FileNotFoundException>(() => storage.GetFileAsync("photo.jpg"));

        Assert.Same(error, result.InnerException);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task StorageFailureIsNotReportedAsMissingObject(HttpStatusCode statusCode)
    {
        var error = new AmazonS3Exception("Ошибка S3.") { StatusCode = statusCode };
        using var storage = CreateStorage(error);

        var result = await Assert.ThrowsAsync<AmazonS3Exception>(() => storage.GetFileAsync("photo.jpg"));

        Assert.Same(error, result);
    }

    private static S3FileStorageProvider CreateStorage(Exception error)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["S3:ServiceUrl"] = "https://storage.example.test",
            ["S3:BucketName"] = "test"
        }).Build();

        var client = new Mock<IAmazonS3>();
        client.Setup(value => value.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(error);

        return new S3FileStorageProvider(configuration, NullLogger<S3FileStorageProvider>.Instance, client.Object);
    }
}
