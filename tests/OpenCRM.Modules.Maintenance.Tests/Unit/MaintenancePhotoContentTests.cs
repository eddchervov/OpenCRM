using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using OpenCRM.Domain.Enums;
using OpenCRM.Modules.Maintenance.Contracts.Photos;
using OpenCRM.Modules.Vehicles.Contracts.Photos;
using OpenCRM.Services.Interfaces.FileStorages;
using OpenCRM.Services.Interfaces.Media;
using OpenCRM.WebApp.Controllers.Api.V1.Media;

namespace OpenCRM.Modules.Maintenance.Tests.Unit;

public sealed class MaintenancePhotoContentTests
{
    private readonly Mock<IMediaAccessService> _media = new(MockBehavior.Strict);
    private readonly Mock<IMaintenancePhotoService> _maintenancePhotos = new(MockBehavior.Strict);
    private readonly Mock<IFileStorageService> _storage = new(MockBehavior.Strict);
    private readonly Guid _organizationId = Guid.NewGuid();
    private readonly Guid _maintenanceId = Guid.NewGuid();
    private readonly Guid _photoId = Guid.NewGuid();

    [Fact]
    public async Task ReturnsOnlyPhotoBelongingToAccessibleMaintenance()
    {
        var controller = this.CreateController();
        var photo = new MaintenancePhotoDto(this._photoId, "maintenance/allowed.jpg", MaintenancePhotoStage.Before, DateTime.UtcNow);
        this._maintenancePhotos.Setup(service => service.GetMaintenancePhotosAsync("owner", this._organizationId, this._maintenanceId))
            .ReturnsAsync(new[] { photo });
        using var stream = new MemoryStream([1, 2, 3]);
        this._storage.Setup(service => service.GetFileAsync(photo.Path)).ReturnsAsync(stream);

        var result = await controller.GetMaintenancePhotoContent(this._maintenanceId, this._photoId, this._organizationId);

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("image/jpeg", file.ContentType);
        Assert.Same(stream, file.FileStream);
        Assert.True(stream.CanRead);
        this._maintenancePhotos.VerifyAll();
        this._storage.VerifyAll();
    }

    [Fact]
    public async Task ForeignPhotoDoesNotReadStorage()
    {
        var controller = this.CreateController();
        var otherPhoto = new MaintenancePhotoDto(Guid.NewGuid(), "maintenance/other.jpg", MaintenancePhotoStage.After, DateTime.UtcNow);
        this._maintenancePhotos.Setup(service => service.GetMaintenancePhotosAsync("owner", this._organizationId, this._maintenanceId))
            .ReturnsAsync(new[] { otherPhoto });

        var result = await controller.GetMaintenancePhotoContent(this._maintenanceId, this._photoId, this._organizationId);

        Assert.IsType<NotFoundResult>(result);
        this._storage.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("Нет доступа к организации", true)]
    [InlineData("Maintenance not found.", false)]
    public async Task DeniedOrMissingMaintenanceDoesNotReadStorage(string message, bool forbidden)
    {
        var controller = this.CreateController();
        this._maintenancePhotos.Setup(service => service.GetMaintenancePhotosAsync("owner", this._organizationId, this._maintenanceId))
            .ThrowsAsync(new InvalidOperationException(message));

        var result = await controller.GetMaintenancePhotoContent(this._maintenanceId, this._photoId, this._organizationId);

        if (forbidden)
        {
            Assert.IsType<ForbidResult>(result);
        }
        else
        {
            Assert.IsType<NotFoundResult>(result);
        }
        this._storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MissingIdentityDoesNotReadPhotosOrStorage()
    {
        var controller = this.CreateController();
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());

        var result = await controller.GetMaintenancePhotoContent(this._maintenanceId, this._photoId, this._organizationId);

        Assert.IsType<UnauthorizedResult>(result);
        this._maintenancePhotos.VerifyNoOtherCalls();
        this._storage.VerifyNoOtherCalls();
    }

    private MediaController CreateController()
    {
        return new MediaController(
            this._media.Object,
            this._maintenancePhotos.Object,
            Mock.Of<IVehicleGalleryService>(),
            Mock.Of<IVehiclePhotoAlbumService>(),
            this._storage.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "owner")], "Test"))
                }
            }
        };
    }
}
