using OpenCRM.Modules.Documents;
using OpenCRM.Modules.Maintenance.Contracts.Photos;
using OpenCRM.Services.Interfaces.FileStorages;
using OpenCRM.Services.Interfaces.Media;
using Moq;

namespace OpenCRM.Modules.Documents.Tests.Unit;

public sealed class DocumentPhotoLoaderTests
{
    [Fact]
    public async Task DeniedPhotoDoesNotAccessPathOrStorage()
    {
        var photoId = Guid.NewGuid();
        var media = new Mock<IMediaAccessService>(MockBehavior.Strict);
        var maintenancePhotos = new Mock<IMaintenancePhotoService>(MockBehavior.Strict);
        var storage = new Mock<IFileStorageService>(MockBehavior.Strict);
        media.Setup(service => service.UserHasAccessToMediaAsync("owner", photoId)).ReturnsAsync(false);
        var loader = new DocumentPhotoLoader(maintenancePhotos.Object, media.Object, storage.Object);

        var result = await loader.GetPhotoDataAsync("owner", photoId);

        Assert.Null(result);
        media.Verify(service => service.UserHasAccessToMediaAsync("owner", photoId), Times.Once);
        media.VerifyNoOtherCalls();
        storage.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpenedStreamIsDisposedExactlyOnceOnSuccessOrReadFailure(bool failReading)
    {
        var photoId = Guid.NewGuid();
        var media = new Mock<IMediaAccessService>(MockBehavior.Strict);
        var maintenancePhotos = new Mock<IMaintenancePhotoService>(MockBehavior.Strict);
        var storage = new Mock<IFileStorageService>(MockBehavior.Strict);
        var photo = new ObservedPhotoStream(failReading);
        media.Setup(service => service.UserHasAccessToMediaAsync("owner", photoId)).ReturnsAsync(true);
        media.Setup(service => service.GetMediaPathAsync("owner", photoId)).ReturnsAsync("photo.png");
        storage.Setup(service => service.FileExistsAsync("photo.png")).ReturnsAsync(true);
        storage.Setup(service => service.GetFileAsync("photo.png")).ReturnsAsync(photo);
        var loader = new DocumentPhotoLoader(maintenancePhotos.Object, media.Object, storage.Object);

        var result = await loader.GetPhotoDataAsync("owner", photoId);

        Assert.Equal(1, photo.DisposeCount);
        if (failReading)
        {
            Assert.Null(result);
        }
        else
        {
            Assert.Equal(new byte[] { 1, 2, 3 }, result);
        }

        media.VerifyAll();
        storage.VerifyAll();
    }

    private sealed class ObservedPhotoStream(bool failReading) : MemoryStream([1, 2, 3])
    {
        public int DisposeCount { get; private set; }

        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            if (failReading)
            {
                throw new IOException("Не удалось прочитать фотографию.");
            }

            return base.CopyToAsync(destination, bufferSize, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            this.DisposeCount++;
            base.Dispose(disposing);
        }
    }
}
