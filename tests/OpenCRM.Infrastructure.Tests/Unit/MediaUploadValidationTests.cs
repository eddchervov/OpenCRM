using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Modules.Inventory.Contracts;
using OpenCRM.Domain.Enums;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Photos;
using OpenCRM.Modules.Vehicles.Contracts.Photos;
using OpenCRM.Services.Interfaces.FileStorages;
using OpenCRM.Services.Interfaces.Media;
using OpenCRM.WebApp.Controllers.Api.V1.Maintenance;
using OpenCRM.WebApp.Controllers.Api.V1.Media;
using Moq;

namespace OpenCRM.Infrastructure.Tests.Unit;

public sealed class MediaUploadValidationTests
{
    private const string UserId = "image-upload-test-user";
    private const string ErrorMessage = "Изображение повреждено или имеет неподдерживаемый формат.";

    [Theory]
    [InlineData("vehicle")]
    [InlineData("vehicle-batch")]
    [InlineData("vehicle-album")]
    [InlineData("maintenance")]
    [InlineData("operation")]
    public async Task CorruptedImageIsReturnedAsBadRequestAcrossUploadRoutes(string route)
    {
        var maintenance = new Mock<IMaintenancePhotoService>(MockBehavior.Strict);
        var gallery = new Mock<IVehicleGalleryService>(MockBehavior.Strict);
        var albums = new Mock<IVehiclePhotoAlbumService>(MockBehavior.Strict);
        var exception = new InvalidDataException(ErrorMessage);
        var organizationId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var maintenanceId = Guid.NewGuid();
        var detailId = Guid.NewGuid();
        var albumId = Guid.NewGuid();
        var files = new FormFileCollection();

        using var content = new MemoryStream([1, 2, 3]);
        files.Add(new FormFile(content, 0, content.Length, "files", "broken.png")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png"
        });

        IActionResult? result = null;
        if (route == "vehicle")
        {
            gallery.Setup(service => service.UploadVehiclePhotoAsync(
                    UserId, organizationId, vehicleId, It.IsAny<Stream>(), "broken.png"))
                .ThrowsAsync(exception);
            var upload = await MediaUploadValidationTests.CreateMediaController(maintenance, gallery, albums)
                .UploadVehiclePhoto(vehicleId, files[0], organizationId);
            result = upload.Result;
            gallery.Verify(service => service.UploadVehiclePhotoAsync(
                UserId, organizationId, vehicleId, It.IsAny<Stream>(), "broken.png"), Times.Once);
            maintenance.VerifyNoOtherCalls();
            albums.VerifyNoOtherCalls();
        }
        else if (route == "vehicle-batch")
        {
            gallery.Setup(service => service.UploadVehiclePhotosAsync(
                    UserId, organizationId, vehicleId, It.IsAny<IEnumerable<(Stream Stream, string FileName)>>()))
                .ThrowsAsync(exception);
            var upload = await MediaUploadValidationTests.CreateMediaController(maintenance, gallery, albums)
                .UploadVehiclePhotos(vehicleId, files, organizationId);
            result = upload.Result;
            gallery.Verify(service => service.UploadVehiclePhotosAsync(
                UserId, organizationId, vehicleId, It.IsAny<IEnumerable<(Stream Stream, string FileName)>>()), Times.Once);
            maintenance.VerifyNoOtherCalls();
            albums.VerifyNoOtherCalls();
        }
        else if (route == "vehicle-album")
        {
            albums.Setup(service => service.UploadVehicleAlbumPhotosAsync(
                    UserId, organizationId, vehicleId, albumId, It.IsAny<IEnumerable<(Stream Stream, string FileName)>>()))
                .ThrowsAsync(exception);
            var upload = await MediaUploadValidationTests.CreateMediaController(maintenance, gallery, albums).UploadVehicleAlbumPhotos(
                vehicleId, albumId, files, organizationId);
            result = upload.Result;
            albums.Verify(service => service.UploadVehicleAlbumPhotosAsync(
                UserId, organizationId, vehicleId, albumId, It.IsAny<IEnumerable<(Stream Stream, string FileName)>>()), Times.Once);
            maintenance.VerifyNoOtherCalls();
            gallery.VerifyNoOtherCalls();
        }
        else if (route == "maintenance")
        {
            maintenance.Setup(service => service.UploadMaintenancePhotosAsync(
                    UserId, organizationId, maintenanceId, It.IsAny<IEnumerable<(Stream Stream, string FileName)>>(),
                    MaintenancePhotoStage.Unspecified))
                .ThrowsAsync(exception);
            result = await MediaUploadValidationTests.CreateMediaController(maintenance, gallery, albums)
                .UploadMaintenancePhotos(maintenanceId, files, organizationId);
            maintenance.Verify(service => service.UploadMaintenancePhotosAsync(
                UserId, organizationId, maintenanceId, It.IsAny<IEnumerable<(Stream Stream, string FileName)>>(),
                MaintenancePhotoStage.Unspecified), Times.Once);
            gallery.VerifyNoOtherCalls();
            albums.VerifyNoOtherCalls();
        }
        else
        {
            maintenance.Setup(service => service.UploadOperationPhotosAsync(
                    UserId, organizationId, maintenanceId, detailId,
                    It.IsAny<IEnumerable<(Stream Stream, string FileName)>>(), MaintenancePhotoStage.Unspecified))
                .ThrowsAsync(exception);
            result = await MediaUploadValidationTests.CreateMaintenanceWorkController(maintenance)
                .UploadPhotos(organizationId, maintenanceId, detailId, files);
            maintenance.Verify(service => service.UploadOperationPhotosAsync(
                UserId, organizationId, maintenanceId, detailId,
                It.IsAny<IEnumerable<(Stream Stream, string FileName)>>(), MaintenancePhotoStage.Unspecified), Times.Once);
            gallery.VerifyNoOtherCalls();
            albums.VerifyNoOtherCalls();
        }

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, badRequest.StatusCode);
        Assert.Equal(ErrorMessage, badRequest.Value?.GetType().GetProperty("message")?.GetValue(badRequest.Value));
    }

    private static MediaController CreateMediaController(
        Mock<IMaintenancePhotoService> maintenance,
        Mock<IVehicleGalleryService> gallery,
        Mock<IVehiclePhotoAlbumService> albums)
    {
        var controller = new MediaController(
            Mock.Of<IMediaAccessService>(), maintenance.Object, gallery.Object, albums.Object, Mock.Of<IFileStorageService>());
        controller.ControllerContext = MediaUploadValidationTests.CreateControllerContext();

        return controller;
    }

    private static MaintenanceWorkApiController CreateMaintenanceWorkController(Mock<IMaintenancePhotoService> maintenance)
    {
        var controller = new MaintenanceWorkApiController(
            Mock.Of<IMaintenanceWorkQueryService>(),
            Mock.Of<IMaintenanceWorkExecutionService>(),
            Mock.Of<IMaintenanceWorkReportService>(),
            Mock.Of<IMaintenanceWorkTimeService>(),
            Mock.Of<IOperationMaterialQueryService>(),
            Mock.Of<IOperationMaterialCommandService>(),
            maintenance.Object,
            Mock.Of<IFileStorageService>());
        controller.ControllerContext = MediaUploadValidationTests.CreateControllerContext();

        return controller;
    }

    private static ControllerContext CreateControllerContext()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("UserID", UserId)], "audit-test"));
        return new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };
    }
}
