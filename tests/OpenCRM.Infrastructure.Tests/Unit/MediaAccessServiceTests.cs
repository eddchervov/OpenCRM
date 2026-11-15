using OpenCRM.Domain.Interfaces.Media;
using OpenCRM.Infrastructure.FileStorage.Media;
using Moq;
using OpenCRM.Services.Interfaces.Media;

namespace OpenCRM.Infrastructure.Tests.Unit;

public sealed class MediaAccessServiceTests
{
    [Fact]
    public async Task GetMediaPathAsyncForwardsEachCallToRepository()
    {
        var userId = "media-reader";
        var mediaId = Guid.NewGuid();
        var repository = new Mock<IGenericMediaRepository>(MockBehavior.Strict);
        repository.SetupSequence(value => value.GetMediaPathAsync(userId, mediaId))
            .ReturnsAsync("first/path.jpg")
            .ReturnsAsync("second/path.jpg");
        var service = new MediaAccessService(repository.Object);

        var firstPath = await service.GetMediaPathAsync(userId, mediaId);
        var secondPath = await service.GetMediaPathAsync(userId, mediaId);

        Assert.Equal("first/path.jpg", firstPath);
        Assert.Equal("second/path.jpg", secondPath);
        repository.Verify(value => value.GetMediaPathAsync(userId, mediaId), Times.Exactly(2));
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UserHasAccessToMediaAsyncForwardsEachCallToRepository()
    {
        var userId = "media-reader";
        var mediaId = Guid.NewGuid();
        var repository = new Mock<IGenericMediaRepository>(MockBehavior.Strict);
        repository.SetupSequence(value => value.UserHasAccessToMediaAsync(userId, mediaId))
            .ReturnsAsync(true)
            .ReturnsAsync(false);
        var service = new MediaAccessService(repository.Object);

        var firstResult = await service.UserHasAccessToMediaAsync(userId, mediaId);
        var secondResult = await service.UserHasAccessToMediaAsync(userId, mediaId);

        Assert.True(firstResult);
        Assert.False(secondResult);
        repository.Verify(value => value.UserHasAccessToMediaAsync(userId, mediaId), Times.Exactly(2));
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetMediaPathAsyncPropagatesRepositoryException()
    {
        var exception = new InvalidOperationException("Repository failure.");
        var repository = new Mock<IGenericMediaRepository>(MockBehavior.Strict);
        repository.Setup(value => value.GetMediaPathAsync("media-reader", It.IsAny<Guid>()))
            .ThrowsAsync(exception);
        var service = new MediaAccessService(repository.Object);

        var actualException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetMediaPathAsync("media-reader", Guid.NewGuid()));

        Assert.Same(exception, actualException);
        repository.Verify(value => value.GetMediaPathAsync("media-reader", It.IsAny<Guid>()), Times.Once);
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UserHasAccessToMediaAsyncPropagatesRepositoryException()
    {
        var exception = new InvalidOperationException("Repository failure.");
        var repository = new Mock<IGenericMediaRepository>(MockBehavior.Strict);
        repository.Setup(value => value.UserHasAccessToMediaAsync("media-reader", It.IsAny<Guid>()))
            .ThrowsAsync(exception);
        var service = new MediaAccessService(repository.Object);

        var actualException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UserHasAccessToMediaAsync("media-reader", Guid.NewGuid()));

        Assert.Same(exception, actualException);
        repository.Verify(value => value.UserHasAccessToMediaAsync("media-reader", It.IsAny<Guid>()), Times.Once);
        repository.VerifyNoOtherCalls();
    }
}
