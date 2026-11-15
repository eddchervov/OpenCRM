using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Domain.Interfaces.Media;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.Repositories;
using OpenCRM.Infrastructure.FileStorage;
using OpenCRM.Infrastructure.FileStorage.Media;
using OpenCRM.Modules.Maintenance.Contracts.Photos;
using OpenCRM.Modules.Vehicles.Contracts.Photos;
using OpenCRM.Services.Interfaces.FileStorages;
using OpenCRM.Services.Interfaces.Media;
using OpenCRM.Testing;
using OpenCRM.WebApp.Controllers.Api.V1.Media;

namespace OpenCRM.Infrastructure.Tests.Integration;

public sealed class MediaAccessIntegrationTests(NotificationFixture fixture) : IClassFixture<NotificationFixture>
{
    [Theory]
    [InlineData("membership")]
    [InlineData("archived")]
    [InlineData("inactive")]
    public async Task OrganizationPhotoAccessIsRecheckedAfterMembershipOrOrganizationChange(string change)
    {
        var data = await this.SeedOrganizationPhotosAsync();
        var storage = new Mock<IFileStorageService>(MockBehavior.Strict);
        storage.Setup(value => value.GeneratePresignedUrlAsync(It.IsAny<string>()))
            .ReturnsAsync((string path) => $"https://storage.example.test/{path}");
        using var host = await MediaAccessIntegrationTests.CreateHostAsync(fixture, data.Reader.Id, storage.Object);
        using var client = host.GetTestServer().CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");

        foreach (var photoId in new[] { data.GalleryPhotoId, data.AlbumPhotoId, data.MaintenancePhotoId })
        {
            using var firstResponse = await client.GetAsync($"/api/v1/media/{photoId}/path");
            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        }

        await using (var context = fixture.CreateContext())
        {
            if (change == "membership")
            {
                await context.OrganizationMembers
                    .Where(member => member.OrganizationId == data.OrganizationId && member.UserId == data.Reader.Id)
                    .ExecuteDeleteAsync();
            }
            else
            {
                var organization = await context.Organizations.SingleAsync(value => value.Id == data.OrganizationId);
                organization.IsActive = change != "inactive";
                organization.IsArchived = change == "archived";
                await context.SaveChangesAsync();
            }
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var mediaAccess = scope.ServiceProvider.GetRequiredService<IMediaAccessService>();
            foreach (var photoId in new[] { data.GalleryPhotoId, data.AlbumPhotoId, data.MaintenancePhotoId })
            {
                Assert.False(await mediaAccess.UserHasAccessToMediaAsync(data.Reader.Id, photoId));
                Assert.Null(await mediaAccess.GetMediaPathAsync(data.Reader.Id, photoId));
            }
        }

        foreach (var photoId in new[] { data.GalleryPhotoId, data.AlbumPhotoId, data.MaintenancePhotoId })
        {
            using var deniedResponse = await client.GetAsync($"/api/v1/media/{photoId}/path");
            Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);
        }

        storage.Verify(value => value.GeneratePresignedUrlAsync(It.IsAny<string>()), Times.Exactly(3));
        storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PersonalPhotoIsAvailableOnlyToItsOwnerAndSystemPhotoPathRemainsNull()
    {
        var owner = new ApplicationUser { UserName = $"media-owner-{Guid.NewGuid():N}@example.test" };
        var foreignUser = new ApplicationUser { UserName = $"media-foreign-{Guid.NewGuid():N}@example.test" };
        Guid photoId;
        Guid systemPhotoId;
        await using (var context = fixture.CreateContext())
        {
            var vehicle = new Vehicle
            {
                User = owner,
                ManualBrandName = "Марка",
                ManualModelName = "Модель",
                RegistrationPlate = "А123АА154"
            };
            var photo = MediaAccessIntegrationTests.CreateVehiclePhoto(vehicle, owner.Id, "personal/photo.jpg");
            var systemPhoto = MediaAccessIntegrationTests.CreateVehiclePhoto(vehicle, null, "system/photo.jpg");
            context.Users.AddRange(owner, foreignUser);
            context.Vehicles.Add(vehicle);
            context.VehiclePhotos.AddRange(photo, systemPhoto);
            await context.SaveChangesAsync();
            photoId = photo.Id;
            systemPhotoId = systemPhoto.Id;
        }

        var storage = new Mock<IFileStorageService>(MockBehavior.Strict);
        storage.Setup(value => value.GeneratePresignedUrlAsync("personal/photo.jpg"))
            .ReturnsAsync("https://storage.example.test/personal/photo.jpg");
        using var ownerHost = await MediaAccessIntegrationTests.CreateHostAsync(fixture, owner.Id, storage.Object);
        using var ownerClient = ownerHost.GetTestServer().CreateClient();
        ownerClient.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");
        using var ownerResponse = await ownerClient.GetAsync($"/api/v1/media/{photoId}/path");
        Assert.Equal(HttpStatusCode.OK, ownerResponse.StatusCode);

        using var foreignHost = await MediaAccessIntegrationTests.CreateHostAsync(fixture, foreignUser.Id, storage.Object);
        using var foreignClient = foreignHost.GetTestServer().CreateClient();
        foreignClient.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");
        using var foreignResponse = await foreignClient.GetAsync($"/api/v1/media/{photoId}/path");
        Assert.Equal(HttpStatusCode.Forbidden, foreignResponse.StatusCode);

        await using var scope = ownerHost.Services.CreateAsyncScope();
        var mediaAccess = scope.ServiceProvider.GetRequiredService<IMediaAccessService>();
        Assert.True(await mediaAccess.UserHasAccessToMediaAsync(foreignUser.Id, systemPhotoId));
        Assert.Null(await mediaAccess.GetMediaPathAsync(foreignUser.Id, systemPhotoId));
        storage.Verify(value => value.GeneratePresignedUrlAsync("personal/photo.jpg"), Times.Once);
        storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DeletedPhotoIsDeniedWithoutRequestingPresignedUrl()
    {
        var data = await this.SeedOrganizationPhotosAsync();
        var storage = new Mock<IFileStorageService>(MockBehavior.Strict);
        storage.Setup(value => value.GeneratePresignedUrlAsync(It.IsAny<string>()))
            .ReturnsAsync("https://storage.example.test/photo.jpg");
        using var host = await MediaAccessIntegrationTests.CreateHostAsync(fixture, data.Reader.Id, storage.Object);
        using var client = host.GetTestServer().CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Authenticated", "true");
        using var initialResponse = await client.GetAsync($"/api/v1/media/{data.GalleryPhotoId}/path");
        Assert.Equal(HttpStatusCode.OK, initialResponse.StatusCode);
        await using (var context = fixture.CreateContext())
        {
            var photo = await context.VehiclePhotos.SingleAsync(value => value.Id == data.GalleryPhotoId);
            photo.IsDeleted = true;
            await context.SaveChangesAsync();
        }

        using var deniedResponse = await client.GetAsync($"/api/v1/media/{data.GalleryPhotoId}/path");

        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);
        storage.Verify(value => value.GeneratePresignedUrlAsync(It.IsAny<string>()), Times.Once);
        storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProductionRegistrationProvidesScopedMediaAccessService()
    {
        var user = new ApplicationUser { UserName = $"media-di-{Guid.NewGuid():N}@example.test" };
        var storage = Mock.Of<IFileStorageService>();
        using var host = await MediaAccessIntegrationTests.CreateHostAsync(fixture, user.Id, storage);

        await using var firstScope = host.Services.CreateAsyncScope();
        await using var secondScope = host.Services.CreateAsyncScope();
        var firstService = firstScope.ServiceProvider.GetRequiredService<IMediaAccessService>();
        var sameScopeService = firstScope.ServiceProvider.GetRequiredService<IMediaAccessService>();
        var secondScopeService = secondScope.ServiceProvider.GetRequiredService<IMediaAccessService>();

        Assert.IsType<MediaAccessService>(firstService);
        Assert.Same(firstService, sameScopeService);
        Assert.NotSame(firstService, secondScopeService);
    }

    private async Task<OrganizationPhotoData> SeedOrganizationPhotosAsync()
    {
        var owner = new ApplicationUser { UserName = $"media-org-owner-{Guid.NewGuid():N}@example.test" };
        var reader = new ApplicationUser { UserName = $"media-org-reader-{Guid.NewGuid():N}@example.test" };
        var organization = new Organization
        {
            Name = "Медиа проверки",
            OwnerUser = owner,
            Members =
            [
                new OrganizationMember { User = owner, RoleCode = OrganizationRoleCodes.Owner },
                new OrganizationMember { User = reader, RoleCode = OrganizationRoleCodes.Employee }
            ]
        };
        var vehicle = new Vehicle
        {
            User = owner,
            Organization = organization,
            ManualBrandName = "Марка",
            ManualModelName = "Модель",
            RegistrationPlate = "В456ВВ154"
        };
        var album = new VehiclePhotoAlbum
        {
            Vehicle = vehicle,
            UserId = owner.Id,
            Title = "Альбом",
            CreatedAt = DateTime.UtcNow
        };
        var galleryPhoto = MediaAccessIntegrationTests.CreateVehiclePhoto(vehicle, owner.Id, "gallery/photo.jpg");
        var albumPhoto = MediaAccessIntegrationTests.CreateVehiclePhoto(vehicle, owner.Id, "album/photo.jpg");
        albumPhoto.Album = album;
        var date = DateTime.UtcNow;
        var maintenance = new Maintenance
        {
            User = owner,
            Vehicle = vehicle,
            Date = date,
            DateTimeCreation = date,
            Type = MaintenanceType.Other,
            Status = MaintenanceStatus.Completed
        };
        var maintenancePhoto = new MaintenancePhoto
        {
            Id = Guid.NewGuid(),
            UserId = owner.Id,
            Vehicle = vehicle,
            Maintenance = maintenance,
            Path = "maintenance/photo.jpg",
            OriginalFileName = "photo.jpg",
            Type = MediaType.MaintenancePhoto,
            CreatedAt = date,
            Size = 123,
            ContentType = "image/jpeg"
        };

        await using var context = fixture.CreateContext();
        context.Users.AddRange(owner, reader);
        context.Organizations.Add(organization);
        context.Vehicles.Add(vehicle);
        context.VehiclePhotoAlbums.Add(album);
        context.VehiclePhotos.AddRange(galleryPhoto, albumPhoto);
        context.Maintenance.Add(maintenance);
        context.MaintenancePhotos.Add(maintenancePhoto);
        await context.SaveChangesAsync();

        return new OrganizationPhotoData(
            reader,
            organization.Id,
            galleryPhoto.Id,
            albumPhoto.Id,
            maintenancePhoto.Id);
    }

    private static VehiclePhoto CreateVehiclePhoto(Vehicle vehicle, string? userId, string path)
    {
        return new VehiclePhoto
        {
            Id = Guid.NewGuid(),
            Vehicle = vehicle,
            UserId = userId,
            Path = path,
            OriginalFileName = Path.GetFileName(path),
            Type = MediaType.VehiclePhoto,
            CreatedAt = DateTime.UtcNow,
            Size = 123,
            ContentType = "image/jpeg"
        };
    }

    private static async Task<IHost> CreateHostAsync(
        NotificationFixture fixture,
        string userId,
        IFileStorageService storage)
    {
        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddScoped(_ => fixture.CreateContext());
                    services.AddScoped<IGenericMediaRepository, GenericMediaRepository>();
                    services.AddOpenCRMFileStorageInfrastructure();
                    services.RemoveAll<IFileStorageService>();
                    services.AddSingleton(storage);
                    services.AddSingleton(Mock.Of<IMaintenancePhotoService>());
                    services.AddSingleton(Mock.Of<IVehicleGalleryService>());
                    services.AddSingleton(Mock.Of<IVehiclePhotoAlbumService>());
                    services.AddAuthentication("MediaAccessTest")
                        .AddScheme<AuthenticationSchemeOptions, MediaAccessTestAuthenticationHandler>(
                            "MediaAccessTest",
                            options => { });
                    services.AddAuthorization();
                    services.AddControllers().AddApplicationPart(typeof(MediaController).Assembly);
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                });
            });

        hostBuilder.ConfigureServices(services => services.AddSingleton(new MediaAccessTestUser(userId)));

        return await hostBuilder.StartAsync();
    }

    private sealed record OrganizationPhotoData(
        ApplicationUser Reader,
        Guid OrganizationId,
        Guid GalleryPhotoId,
        Guid AlbumPhotoId,
        Guid MaintenancePhotoId);

    private sealed record MediaAccessTestUser(string Id);

    private sealed class MediaAccessTestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        MediaAccessTestUser user) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!this.Request.Headers.TryGetValue("X-Test-Authenticated", out var value) || value != "true")
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, user.Id)],
                this.Scheme.Name);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), this.Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
