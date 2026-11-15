using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using OpenCRM.Modules.Maintenance.Contracts.Photos;
using OpenCRM.Modules.Vehicles.Contracts.Photos;
using OpenCRM.Services.Interfaces.FileStorages;
using OpenCRM.Services.Interfaces.Media;
using OpenCRM.WebApp.Controllers.Api.V1.Media;

namespace OpenCRM.Modules.Vehicles.Tests.Integration;

public sealed class VehiclePhotoHttpAcceptanceTests
{
    private const string AuthenticationScheme = "VehiclePhotosTest";

    [Fact]
    public async Task GalleryReadEndpointsUseTheVehicleGalleryContractAndReturnPresignedUrls()
    {
        var userId = Guid.NewGuid().ToString();
        var vehicleId = Guid.NewGuid();
        var galleryPhotoId = Guid.NewGuid();
        var gallery = new Mock<IVehicleGalleryService>(MockBehavior.Strict);
        var media = new Mock<IMediaAccessService>();
        var storage = new Mock<IFileStorageService>();
        gallery.Setup(service => service.GetVehiclePhotosAsync(userId, null, vehicleId))
            .ReturnsAsync(
            [
                new VehiclePhotoDto
                {
                    PhotoId = galleryPhotoId,
                    VehicleId = vehicleId,
                    IsPrimary = true,
                    OriginalFileName = "main.jpg",
                    Size = 123,
                    ContentType = "image/jpeg",
                    CreatedAt = DateTime.UtcNow
                }
            ]);
        gallery.Setup(service => service.GetVehiclePhotosAsync(userId, null, It.Is<IEnumerable<Guid>>(ids => ids.SequenceEqual(new[] { vehicleId }))))
            .ReturnsAsync([new VehiclePhotoDto { PhotoId = galleryPhotoId, VehicleId = vehicleId, IsPrimary = true }]);
        media.Setup(service => service.GetMediaPathAsync(userId, galleryPhotoId)).ReturnsAsync("vehicles/gallery.jpg");
        storage.Setup(service => service.GeneratePresignedUrlAsync("vehicles/gallery.jpg"))
            .ReturnsAsync("https://storage.test/vehicles/gallery.jpg");

        using var server = VehiclePhotoHttpAcceptanceTests.CreateServer(
            gallery.Object, Mock.Of<IVehiclePhotoAlbumService>(), media.Object, storage.Object);
        using var client = VehiclePhotoHttpAcceptanceTests.CreateAuthenticatedClient(server, userId);

        var galleryResponse = await client.GetAsync($"/api/v1/media/vehicles/{vehicleId:D}/photos");
        Assert.Equal(HttpStatusCode.OK, galleryResponse.StatusCode);
        using var galleryJson = JsonDocument.Parse(await galleryResponse.Content.ReadAsStringAsync());
        var galleryPhoto = Assert.Single(galleryJson.RootElement.EnumerateArray());
        Assert.Equal(galleryPhotoId, galleryPhoto.GetProperty("photoId").GetGuid());
        Assert.Equal("https://storage.test/vehicles/gallery.jpg", galleryPhoto.GetProperty("url").GetString());

        var groupedResponse = await client.GetAsync($"/api/v1/media/vehicles/photos?vehicleIds={vehicleId:D}");
        Assert.Equal(HttpStatusCode.OK, groupedResponse.StatusCode);
        using var groupedJson = JsonDocument.Parse(await groupedResponse.Content.ReadAsStringAsync());
        Assert.Equal(vehicleId, groupedJson.RootElement[0].GetProperty("vehicleId").GetGuid());
        Assert.Equal(galleryPhotoId, groupedJson.RootElement[0].GetProperty("photos")[0].GetProperty("photoId").GetGuid());

        gallery.Verify(service => service.GetVehiclePhotosAsync(userId, null, vehicleId), Times.Once);
        gallery.Verify(service => service.GetVehiclePhotosAsync(userId, null, It.IsAny<IEnumerable<Guid>>()), Times.Once);
    }

    [Fact]
    public async Task AlbumReadAndCreateEndpointsReturnPhotoUrlsAndPlanLimits()
    {
        var userId = Guid.NewGuid().ToString();
        var vehicleId = Guid.NewGuid();
        var albumId = Guid.NewGuid();
        var photoId = Guid.NewGuid();
        var albums = new Mock<IVehiclePhotoAlbumService>(MockBehavior.Strict);
        var media = new Mock<IMediaAccessService>();
        var storage = new Mock<IFileStorageService>();
        var album = new VehiclePhotoAlbumDto
        {
            AlbumId = albumId,
            VehicleId = vehicleId,
            Title = "Документы",
            PhotoCount = 1,
            PrimaryPhotoId = photoId,
            Photos = [new VehiclePhotoDto { PhotoId = photoId, VehicleId = vehicleId, AlbumId = albumId, IsPrimary = true }]
        };
        albums.Setup(service => service.GetVehiclePhotoAlbumsAsync(userId, null, vehicleId)).ReturnsAsync([album]);
        albums.Setup(service => service.GetVehiclePhotoAlbumLimitAsync(userId, null, vehicleId))
            .ReturnsAsync(new VehiclePhotoAlbumLimitDto { CurrentCount = 1, MaxCount = 1, IsLimitedByPlan = true });
        albums.Setup(service => service.GetVehiclePhotoAlbumAsync(userId, null, vehicleId, albumId)).ReturnsAsync(album);
        albums.Setup(service => service.CreateVehiclePhotoAlbumAsync(userId, null, vehicleId, "Осмотр"))
            .ReturnsAsync(new VehiclePhotoAlbumDto { AlbumId = albumId, VehicleId = vehicleId, Title = "Осмотр" });
        media.Setup(service => service.GetMediaPathAsync(userId, photoId)).ReturnsAsync("vehicles/albums/photo.jpg");
        storage.Setup(service => service.GeneratePresignedUrlAsync("vehicles/albums/photo.jpg"))
            .ReturnsAsync("https://storage.test/vehicles/albums/photo.jpg");

        using var server = VehiclePhotoHttpAcceptanceTests.CreateServer(
            Mock.Of<IVehicleGalleryService>(), albums.Object, media.Object, storage.Object);
        using var client = VehiclePhotoHttpAcceptanceTests.CreateAuthenticatedClient(server, userId);

        var albumsResponse = await client.GetAsync($"/api/v1/media/vehicles/{vehicleId:D}/albums");
        Assert.Equal(HttpStatusCode.OK, albumsResponse.StatusCode);
        using var albumsJson = JsonDocument.Parse(await albumsResponse.Content.ReadAsStringAsync());
        Assert.Equal(1, albumsJson.RootElement.GetProperty("limit").GetProperty("maxCount").GetInt32());
        var albumJson = Assert.Single(albumsJson.RootElement.GetProperty("albums").EnumerateArray());
        Assert.Equal(albumId, albumJson.GetProperty("albumId").GetGuid());
        Assert.Equal("Документы", albumJson.GetProperty("title").GetString());
        Assert.Equal("https://storage.test/vehicles/albums/photo.jpg", albumJson.GetProperty("photos")[0].GetProperty("url").GetString());

        var singleAlbumResponse = await client.GetAsync($"/api/v1/media/vehicles/{vehicleId:D}/albums/{albumId:D}");
        Assert.Equal(HttpStatusCode.OK, singleAlbumResponse.StatusCode);
        using var singleAlbumJson = JsonDocument.Parse(await singleAlbumResponse.Content.ReadAsStringAsync());
        Assert.Equal(albumId, singleAlbumJson.RootElement.GetProperty("albumId").GetGuid());

        var createResponse = await client.PostAsJsonAsync($"/api/v1/media/vehicles/{vehicleId:D}/albums", new { title = "Осмотр" });
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        using var createJson = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        Assert.Equal("Осмотр", createJson.RootElement.GetProperty("title").GetString());

        albums.Verify(service => service.GetVehiclePhotoAlbumsAsync(userId, null, vehicleId), Times.Once);
        albums.Verify(service => service.GetVehiclePhotoAlbumLimitAsync(userId, null, vehicleId), Times.Once);
        albums.Verify(service => service.GetVehiclePhotoAlbumAsync(userId, null, vehicleId, albumId), Times.Once);
        albums.Verify(service => service.CreateVehiclePhotoAlbumAsync(userId, null, vehicleId, "Осмотр"), Times.Once);
    }

    [Fact]
    public async Task UploadEndpointsSendFilesToGalleryAndAlbumServices()
    {
        var userId = Guid.NewGuid().ToString();
        var vehicleId = Guid.NewGuid();
        var albumId = Guid.NewGuid();
        var uploadedPhotoId = Guid.NewGuid();
        var gallery = new Mock<IVehicleGalleryService>(MockBehavior.Strict);
        var albums = new Mock<IVehiclePhotoAlbumService>(MockBehavior.Strict);
        gallery.Setup(service => service.UploadVehiclePhotoAsync(userId, null, vehicleId, It.IsAny<Stream>(), "single.jpg"))
            .ReturnsAsync(uploadedPhotoId);
        gallery.Setup(service => service.UploadVehiclePhotosAsync(userId, null, vehicleId,
                It.Is<IEnumerable<(Stream Stream, string FileName)>>(files => files.Count() == 1)))
            .ReturnsAsync([uploadedPhotoId]);
        albums.Setup(service => service.UploadVehicleAlbumPhotosAsync(userId, null, vehicleId, albumId,
                It.Is<IEnumerable<(Stream Stream, string FileName)>>(files => files.Count() == 1)))
            .ReturnsAsync([uploadedPhotoId]);
        using var server = VehiclePhotoHttpAcceptanceTests.CreateServer(
            gallery.Object, albums.Object, Mock.Of<IMediaAccessService>(), Mock.Of<IFileStorageService>());
        using var client = VehiclePhotoHttpAcceptanceTests.CreateAuthenticatedClient(server, userId);

        using var singlePhotoForm = VehiclePhotoHttpAcceptanceTests.CreateImageForm("single.jpg", "formFile");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/v1/media/vehicles/{vehicleId:D}/photo", singlePhotoForm)).StatusCode);
        using var galleryPhotoForm = VehiclePhotoHttpAcceptanceTests.CreateImageForm("bulk.jpg");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/v1/media/vehicles/{vehicleId:D}/photos", galleryPhotoForm)).StatusCode);
        using var albumPhotoForm = VehiclePhotoHttpAcceptanceTests.CreateImageForm("album.jpg");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/v1/media/vehicles/{vehicleId:D}/albums/{albumId:D}/photos", albumPhotoForm)).StatusCode);

        gallery.Verify(service => service.UploadVehiclePhotoAsync(userId, null, vehicleId, It.IsAny<Stream>(), "single.jpg"), Times.Once);
        gallery.Verify(service => service.UploadVehiclePhotosAsync(userId, null, vehicleId, It.IsAny<IEnumerable<(Stream Stream, string FileName)>>()), Times.Once);
        albums.Verify(service => service.UploadVehicleAlbumPhotosAsync(userId, null, vehicleId, albumId,
            It.IsAny<IEnumerable<(Stream Stream, string FileName)>>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAndPrimaryRoutesCallSeparateGalleryAndAlbumServices()
    {
        var userId = Guid.NewGuid().ToString();
        var vehicleId = Guid.NewGuid();
        var albumId = Guid.NewGuid();
        var photoId = Guid.NewGuid();
        var gallery = new Mock<IVehicleGalleryService>(MockBehavior.Strict);
        var albums = new Mock<IVehiclePhotoAlbumService>(MockBehavior.Strict);
        gallery.Setup(service => service.DeleteVehiclePhotoAsync(userId, null, vehicleId)).Returns(Task.CompletedTask);
        gallery.Setup(service => service.DeleteVehiclePhotoAsync(userId, null, vehicleId, photoId)).Returns(Task.CompletedTask);
        gallery.Setup(service => service.SetPrimaryVehiclePhotoAsync(userId, null, vehicleId, photoId)).Returns(Task.CompletedTask);
        albums.Setup(service => service.DeleteVehiclePhotoAlbumAsync(userId, null, vehicleId, albumId)).Returns(Task.CompletedTask);
        albums.Setup(service => service.DeleteVehicleAlbumPhotoAsync(userId, null, vehicleId, albumId, photoId)).Returns(Task.CompletedTask);
        albums.Setup(service => service.SetPrimaryVehicleAlbumPhotoAsync(userId, null, vehicleId, albumId, photoId)).Returns(Task.CompletedTask);
        using var server = VehiclePhotoHttpAcceptanceTests.CreateServer(
            gallery.Object, albums.Object, Mock.Of<IMediaAccessService>(), Mock.Of<IFileStorageService>());
        using var client = VehiclePhotoHttpAcceptanceTests.CreateAuthenticatedClient(server, userId);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/media/vehicles/{vehicleId:D}/photo")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/media/vehicles/{vehicleId:D}/photos/{photoId:D}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/v1/media/vehicles/{vehicleId:D}/photos/{photoId:D}/primary", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/media/vehicles/{vehicleId:D}/albums/{albumId:D}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/media/vehicles/{vehicleId:D}/albums/{albumId:D}/photos/{photoId:D}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PostAsync($"/api/v1/media/vehicles/{vehicleId:D}/albums/{albumId:D}/photos/{photoId:D}/primary", null)).StatusCode);

        gallery.Verify(service => service.DeleteVehiclePhotoAsync(userId, null, vehicleId), Times.Once);
        gallery.Verify(service => service.DeleteVehiclePhotoAsync(userId, null, vehicleId, photoId), Times.Once);
        gallery.Verify(service => service.SetPrimaryVehiclePhotoAsync(userId, null, vehicleId, photoId), Times.Once);
        albums.Verify(service => service.DeleteVehiclePhotoAlbumAsync(userId, null, vehicleId, albumId), Times.Once);
        albums.Verify(service => service.DeleteVehicleAlbumPhotoAsync(userId, null, vehicleId, albumId, photoId), Times.Once);
        albums.Verify(service => service.SetPrimaryVehicleAlbumPhotoAsync(userId, null, vehicleId, albumId, photoId), Times.Once);
    }

    [Fact]
    public async Task GalleryAndAlbumRoutesEnforceAuthenticationAndTranslateAccessOrMissingResources()
    {
        var userId = Guid.NewGuid().ToString();
        var vehicleId = Guid.NewGuid();
        var albumId = Guid.NewGuid();
        var gallery = new Mock<IVehicleGalleryService>();
        var albums = new Mock<IVehiclePhotoAlbumService>();
        gallery.Setup(service => service.GetVehiclePhotosAsync(userId, null, vehicleId))
            .ThrowsAsync(new InvalidOperationException("У вас нет доступа к этой организации."));
        albums.Setup(service => service.GetVehiclePhotoAlbumAsync(userId, null, vehicleId, albumId))
            .ThrowsAsync(new ArgumentException("Vehicle photo album not found."));

        using var server = VehiclePhotoHttpAcceptanceTests.CreateServer(
            gallery.Object,
            albums.Object,
            Mock.Of<IMediaAccessService>(),
            Mock.Of<IFileStorageService>());
        using var anonymousClient = server.GetTestClient();
        using var authenticatedClient = VehiclePhotoHttpAcceptanceTests.CreateAuthenticatedClient(server, userId);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymousClient.GetAsync($"/api/v1/media/vehicles/{vehicleId:D}/photos")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await authenticatedClient.GetAsync($"/api/v1/media/vehicles/{vehicleId:D}/photos")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await authenticatedClient.GetAsync($"/api/v1/media/vehicles/{vehicleId:D}/albums/{albumId:D}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await authenticatedClient.GetAsync("/api/v1/media/vehicles/photos")).StatusCode);
    }

    private static IHost CreateServer(
        IVehicleGalleryService galleryService,
        IVehiclePhotoAlbumService albumService,
        IMediaAccessService mediaService,
        IFileStorageService fileStorageService)
    {
        var builder = new HostBuilder()
            .ConfigureWebHost(web => web.UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddSingleton(galleryService);
                    services.AddSingleton(albumService);
                    services.AddSingleton(mediaService);
                    services.AddSingleton(fileStorageService);
                    services.AddSingleton(Mock.Of<IMaintenancePhotoService>());
                    services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = VehiclePhotoHttpAcceptanceTests.AuthenticationScheme;
                        options.DefaultChallengeScheme = VehiclePhotoHttpAcceptanceTests.AuthenticationScheme;
                        options.DefaultForbidScheme = VehiclePhotoHttpAcceptanceTests.AuthenticationScheme;
                    }).AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>(
                        VehiclePhotoHttpAcceptanceTests.AuthenticationScheme,
                        _ => { });
                    services.AddAuthorization();
                    services.AddControllers().AddApplicationPart(typeof(MediaController).Assembly);
                })
                .Configure(application =>
                {
                    application.UseRouting();
                    application.UseAuthentication();
                    application.UseAuthorization();
                    application.UseEndpoints(endpoints => endpoints.MapControllers());
                }));

        var host = builder.Build();
        host.Start();

        return host;
    }

    private static HttpClient CreateAuthenticatedClient(IHost server, string userId)
    {
        var client = server.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", userId);

        return client;
    }

    private static MultipartFormDataContent CreateImageForm(string fileName, string formField = "formFiles")
    {
        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent([1, 2, 3]);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(content, formField, fileName);

        return form;
    }

    private sealed class HeaderAuthenticationHandler(
        Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var userId = this.Request.Headers["X-Test-User"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim("UserID", userId)
            };
            var identity = new ClaimsIdentity(claims, this.Scheme.Name);
            var principal = new ClaimsPrincipal(identity);

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, this.Scheme.Name)));
        }
    }
}
