using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Services.Interfaces.Media.Dto;
using OpenCRM.Services.Interfaces.Subscriptions.Dtos;

namespace OpenCRM.Modules.Vehicles.Tests.Integration;

public sealed class VehicleGalleryServiceTests(VehicleDatabaseFixture fixture) : IClassFixture<VehicleDatabaseFixture>
{
    [Fact]
    public async Task UploadingFirstPhotosSetsOnlyFirstAsPrimaryAndWritesHistory()
    {
        await using var context = fixture.CreateContext();
        var (user, vehicle) = await VehiclePhotoServiceTestSupport.SeedPersonalVehicleAsync(context);
        using var services = new VehiclePhotoServiceTestSupport(context);
        using var firstStream = new MemoryStream([1, 2, 3]);
        using var secondStream = new MemoryStream([4, 5]);

        var photoIds = await services.Gallery.UploadVehiclePhotosAsync(user.Id, null, vehicle.Id,
        [
            (firstStream, "переднее.jpg"),
            (secondStream, "заднее.jpg")
        ]);

        var photos = await context.VehiclePhotos
            .Where(photo => photo.VehicleId == vehicle.Id && photo.AlbumId == null)
            .ToDictionaryAsync(photo => photo.Id);
        Assert.Equal(2, photoIds.Count);
        Assert.Equal(photoIds.Order(), photos.Keys.Order());
        Assert.True(photos[photoIds[0]].IsPrimary);
        Assert.False(photos[photoIds[1]].IsPrimary);
        Assert.Equal(user.Id, photos[photoIds[0]].UserId);
        Assert.Equal("image/jpeg", photos[photoIds[0]].ContentType);
        Assert.Equal(3, photos[photoIds[0]].Size);
        Assert.Equal("переднее.jpg", photos[photoIds[0]].OriginalFileName);
        var history = await context.VehicleHistoryEvents.SingleAsync(item => item.VehicleId == vehicle.Id);
        Assert.Equal("vehicle_photo_uploaded", history.EventType);
        Assert.Equal("Фотографии техники загружены", history.Title);
        Assert.Equal("Файлы: переднее.jpg, заднее.jpg", history.Description);
        services.ImageProcessing.Verify(processor => processor.ProcessAndSaveImageAsync(
            It.IsAny<Stream>(),
            It.Is<ImageProcessingOptions>(options => options.TargetFolder == $"img/users/{user.Id}/vehicles"
                && options.MaxWidth == 1200 && options.MaxHeight == 800 && options.Quality == 70)), Times.Exactly(2));
    }

    [Fact]
    public async Task SingleUploadOverloadsUsePersonalAndOrganizationFolders()
    {
        await using var personalContext = fixture.CreateContext();
        var (personalUser, personalVehicle) = await VehiclePhotoServiceTestSupport.SeedPersonalVehicleAsync(personalContext);
        using var personalServices = new VehiclePhotoServiceTestSupport(personalContext);
        using var personalStream = new MemoryStream([1]);

        var personalPhotoId = await personalServices.Gallery.UploadVehiclePhotoAsync(
            personalUser.Id, personalVehicle.Id, personalStream, "личное.jpg");

        var personalPhoto = await personalContext.VehiclePhotos.SingleAsync(photo => photo.Id == personalPhotoId);
        Assert.True(personalPhoto.IsPrimary);
        personalServices.ImageProcessing.Verify(processor => processor.ProcessAndSaveImageAsync(
            It.IsAny<Stream>(), It.Is<ImageProcessingOptions>(options => options.TargetFolder == $"img/users/{personalUser.Id}/vehicles")), Times.Once);

        await using var organizationContext = fixture.CreateContext();
        var (organizationUser, organization, organizationVehicle) = await VehiclePhotoServiceTestSupport.SeedOrganizationVehicleAsync(organizationContext);
        using var organizationServices = new VehiclePhotoServiceTestSupport(organizationContext);
        using var organizationStream = new MemoryStream([2]);

        var organizationPhotoId = await organizationServices.Gallery.UploadVehiclePhotoAsync(
            organizationUser.Id, organization.Id, organizationVehicle.Id, organizationStream, "рабочее.jpg");

        var organizationPhoto = await organizationContext.VehiclePhotos.SingleAsync(photo => photo.Id == organizationPhotoId);
        Assert.Equal(organization.Id, await organizationContext.VehicleHistoryEvents
            .Where(item => item.VehicleId == organizationVehicle.Id)
            .Select(item => item.OrganizationId)
            .SingleAsync());
        Assert.True(organizationPhoto.IsPrimary);
        organizationServices.ImageProcessing.Verify(processor => processor.ProcessAndSaveImageAsync(
            It.IsAny<Stream>(), It.Is<ImageProcessingOptions>(options => options.TargetFolder == $"img/organizations/{organization.Id:D}/vehicles")), Times.Once);
        organizationServices.OrganizationSubscriptions.Verify(service => service.EnsureWorkspaceCanWriteAsync(
            organizationUser.Id, organization.Id), Times.Once);
    }

    [Fact]
    public async Task FreeOrganizationCanManageVehicleGalleryPhotosWithoutPhotoAlbumFeature()
    {
        await using var context = fixture.CreateContext();
        var (user, organization, vehicle) = await VehiclePhotoServiceTestSupport.SeedOrganizationVehicleAsync(context);
        var subscriptions = new Mock<OpenCRM.Services.Interfaces.Organizations.IOrganizationSubscriptionService>();
        subscriptions.Setup(service => service.HasFeatureAsync(user.Id, organization.Id, Helpers.FeatureCodes.VehiclePhotoAlbums))
            .ReturnsAsync(false);
        subscriptions.Setup(service => service.GetEffectivePlanAsync(user.Id, organization.Id))
            .ReturnsAsync(new SubscriptionPlanDto { IsDefaultPlan = true });
        using var services = new VehiclePhotoServiceTestSupport(context, organizationSubscriptions: subscriptions);
        using var firstStream = new MemoryStream([1]);
        using var secondStream = new MemoryStream([2]);

        var photoIds = await services.Gallery.UploadVehiclePhotosAsync(
            user.Id,
            organization.Id,
            vehicle.Id,
            [(firstStream, "первое.jpg"), (secondStream, "второе.jpg")]);
        await services.Gallery.SetPrimaryVehiclePhotoAsync(user.Id, organization.Id, vehicle.Id, photoIds[1]);
        await services.Gallery.DeleteVehiclePhotoAsync(user.Id, organization.Id, vehicle.Id, photoIds[0]);

        var photos = await context.VehiclePhotos.Where(photo => photo.VehicleId == vehicle.Id).ToListAsync();
        Assert.True(photos.Single(photo => photo.Id == photoIds[1]).IsPrimary);
        Assert.True(photos.Single(photo => photo.Id == photoIds[0]).IsDeleted);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            services.Albums.CreateVehiclePhotoAlbumAsync(user.Id, organization.Id, vehicle.Id, "Альбом Free"));
        subscriptions.Verify(service => service.HasFeatureAsync(user.Id, organization.Id, Helpers.FeatureCodes.VehiclePhotoAlbums), Times.Once);
    }

    [Fact]
    public async Task UploadStartsAsPrimaryWhenExistingGalleryHasNoPrimaryPhoto()
    {
        await using var context = fixture.CreateContext();
        var (user, vehicle) = await VehiclePhotoServiceTestSupport.SeedPersonalVehicleAsync(context);
        var existingPhoto = VehiclePhotoServiceTestSupport.CreatePhoto(vehicle, "old.jpg", false);
        context.VehiclePhotos.Add(existingPhoto);
        await context.SaveChangesAsync();
        using var services = new VehiclePhotoServiceTestSupport(context);
        using var stream = new MemoryStream([1]);

        var ids = await services.Gallery.UploadVehiclePhotosAsync(user.Id, null, vehicle.Id, [(stream, "new.jpg")]);

        Assert.True((await context.VehiclePhotos.SingleAsync(photo => photo.Id == ids[0])).IsPrimary);
        Assert.False((await context.VehiclePhotos.SingleAsync(photo => photo.Id == existingPhoto.Id)).IsPrimary);

        using var nextStream = new MemoryStream([2]);
        var nextId = Assert.Single(await services.Gallery.UploadVehiclePhotosAsync(user.Id, null, vehicle.Id, [(nextStream, "next.jpg")]));
        Assert.False(await context.VehiclePhotos.Where(photo => photo.Id == nextId).Select(photo => photo.IsPrimary).SingleAsync());
    }

    [Fact]
    public async Task UploadRejectsEmptyBatchAndMoreThanTenGalleryPhotosBeforeProcessing()
    {
        await using var context = fixture.CreateContext();
        var (user, vehicle) = await VehiclePhotoServiceTestSupport.SeedPersonalVehicleAsync(context);
        var photos = Enumerable.Range(0, 10)
            .Select(index => VehiclePhotoServiceTestSupport.CreatePhoto(vehicle, $"{index}.jpg", index == 0))
            .ToArray();
        context.VehiclePhotos.AddRange(photos);
        await context.SaveChangesAsync();
        using var services = new VehiclePhotoServiceTestSupport(context);

        var emptyError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            services.Gallery.UploadVehiclePhotosAsync(user.Id, null, vehicle.Id, []));
        using var stream = new MemoryStream([1]);
        var limitError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            services.Gallery.UploadVehiclePhotosAsync(user.Id, null, vehicle.Id, [(stream, "11.jpg")]));

        Assert.Contains("Выберите фотографии", emptyError.Message);
        Assert.Contains("максимум 10", limitError.Message);
        services.ImageProcessing.Verify(processor => processor.ProcessAndSaveImageAsync(It.IsAny<Stream>(), It.IsAny<ImageProcessingOptions>()), Times.Never);
    }

    [Fact]
    public async Task UploadRejectsVehicleOutsidePersonalWorkspaceOrWhenVehicleDoesNotExist()
    {
        await using var context = fixture.CreateContext();
        var (owner, vehicle) = await VehiclePhotoServiceTestSupport.SeedPersonalVehicleAsync(context);
        var (otherUser, _) = await VehiclePhotoServiceTestSupport.SeedPersonalVehicleAsync(context);
        using var services = new VehiclePhotoServiceTestSupport(context);
        using var stream = new MemoryStream([1]);

        await Assert.ThrowsAsync<ArgumentException>(() => services.Gallery.UploadVehiclePhotoAsync(otherUser.Id, vehicle.Id, stream, "чужое.jpg"));
        await Assert.ThrowsAsync<ArgumentException>(() => services.Gallery.UploadVehiclePhotoAsync(owner.Id, Guid.NewGuid(), stream, "нет.jpg"));

        services.ImageProcessing.Verify(processor => processor.ProcessAndSaveImageAsync(It.IsAny<Stream>(), It.IsAny<ImageProcessingOptions>()), Times.Never);
    }

    [Fact]
    public async Task PartialStorageFailureDoesNotPersistPhotoRowsOrAttemptStorageCleanup()
    {
        await using var context = fixture.CreateContext();
        var (user, vehicle) = await VehiclePhotoServiceTestSupport.SeedPersonalVehicleAsync(context);
        var imageProcessing = VehiclePhotoServiceTestSupport.CreateImageProcessingMock();
        imageProcessing.SetupSequence(processor => processor.ProcessAndSaveImageAsync(It.IsAny<Stream>(), It.IsAny<ImageProcessingOptions>()))
            .ReturnsAsync("img/shared/first.jpg")
            .ThrowsAsync(new IOException("Ошибка хранилища при обработке второго фото."));
        using var services = new VehiclePhotoServiceTestSupport(context, imageProcessing);
        using var firstStream = new MemoryStream([1]);
        using var secondStream = new MemoryStream([2]);

        await Assert.ThrowsAsync<IOException>(() => services.Gallery.UploadVehiclePhotosAsync(user.Id, null, vehicle.Id,
        [
            (firstStream, "first.jpg"),
            (secondStream, "second.jpg")
        ]));

        Assert.Empty(await context.VehiclePhotos.Where(photo => photo.VehicleId == vehicle.Id).ToListAsync());
        imageProcessing.Verify(processor => processor.ProcessAndSaveImageAsync(It.IsAny<Stream>(), It.IsAny<ImageProcessingOptions>()), Times.Exactly(2));
        imageProcessing.Verify(processor => processor.GetImageFormatAsync(It.IsAny<Stream>()), Times.Once);
    }

    [Fact]
    public async Task PhotoRowsRemainWhenHistorySaveFailsAfterStorageAndPhotoSave()
    {
        await using var seedContext = fixture.CreateContext();
        var (user, vehicle) = await VehiclePhotoServiceTestSupport.SeedPersonalVehicleAsync(seedContext);
        await using var context = fixture.CreateContext(new VehiclePhotoServiceTestSupport.FailOnSaveInterceptor(2));
        using var services = new VehiclePhotoServiceTestSupport(context);
        using var stream = new MemoryStream([1]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => services.Gallery.UploadVehiclePhotoAsync(user.Id, vehicle.Id, stream, "photo.jpg"));

        await using var verificationContext = fixture.CreateContext();
        var persistedPhoto = await verificationContext.VehiclePhotos.SingleAsync(photo => photo.VehicleId == vehicle.Id);
        Assert.Equal("photo.jpg", persistedPhoto.OriginalFileName);
        Assert.Empty(await verificationContext.VehicleHistoryEvents.Where(item => item.VehicleId == vehicle.Id).ToListAsync());
    }

    [Fact]
    public async Task HistoryDescriptionDatabaseLimitPreservesPreviouslySavedPhotoAndPath()
    {
        await using var seedContext = fixture.CreateContext();
        var (user, vehicle) = await VehiclePhotoServiceTestSupport.SeedPersonalVehicleAsync(seedContext);
        await using var context = fixture.CreateContext();
        using var services = new VehiclePhotoServiceTestSupport(context);
        var fileNames = Enumerable.Range(0, 5)
            .Select(index => $"{index}{new string('a', 245)}.jpg")
            .ToArray();
        var streams = fileNames.Select(_ => new MemoryStream([1])).ToArray();

        await Assert.ThrowsAsync<DbUpdateException>(() => services.Gallery.UploadVehiclePhotosAsync(
            user.Id,
            null,
            vehicle.Id,
            streams.Select((stream, index) => ((Stream)stream, fileNames[index]))));
        foreach (var stream in streams)
        {
            stream.Dispose();
        }

        await using var verificationContext = fixture.CreateContext();
        var persistedPhotos = await verificationContext.VehiclePhotos.Where(photo => photo.VehicleId == vehicle.Id).ToListAsync();
        Assert.Equal(fileNames.Length, persistedPhotos.Count);
        Assert.All(persistedPhotos, photo => Assert.StartsWith("img/vehicles/", photo.Path));
        Assert.Equal(fileNames.OrderBy(name => name), persistedPhotos.Select(photo => photo.OriginalFileName).OrderBy(name => name));
        Assert.Empty(await verificationContext.VehicleHistoryEvents.Where(item => item.VehicleId == vehicle.Id).ToListAsync());
    }

    [Fact]
    public async Task GetGalleryAndBulkPhotosMapsRowsDeduplicatesVehicleIdsAndAllowsEmptyBulkRequest()
    {
        await using var context = fixture.CreateContext();
        var (user, vehicle) = await VehiclePhotoServiceTestSupport.SeedPersonalVehicleAsync(context);
        var otherVehicle = new Vehicle { User = user, UserId = user.Id, DateTimeCreation = DateTime.UtcNow };
        context.Vehicles.Add(otherVehicle);
        var first = VehiclePhotoServiceTestSupport.CreatePhoto(vehicle, "first.jpg", true);
        var second = VehiclePhotoServiceTestSupport.CreatePhoto(otherVehicle, "second.jpg", true);
        var album = VehiclePhotoServiceTestSupport.CreateAlbum(vehicle);
        var albumPhoto = VehiclePhotoServiceTestSupport.CreatePhoto(vehicle, "album.jpg", true, album.Id);
        context.AddRange(first, second, album, albumPhoto);
        await context.SaveChangesAsync();
        using var services = new VehiclePhotoServiceTestSupport(context);

        var singleVehiclePhotos = await services.Gallery.GetVehiclePhotosAsync(user.Id, null, vehicle.Id);
        var bulkPhotos = await services.Gallery.GetVehiclePhotosAsync(user.Id, null, [vehicle.Id, vehicle.Id, otherVehicle.Id]);
        var emptyBulk = await services.Gallery.GetVehiclePhotosAsync(user.Id, null, Array.Empty<Guid>());

        await Assert.ThrowsAsync<ArgumentException>(() => services.Gallery.GetVehiclePhotosAsync(user.Id, null, Guid.NewGuid()));

        var mappedPhoto = Assert.Single(singleVehiclePhotos);
        Assert.Equal(first.Id, mappedPhoto.PhotoId);
        Assert.Equal(vehicle.Id, mappedPhoto.VehicleId);
        Assert.Null(mappedPhoto.AlbumId);
        Assert.Equal("first.jpg", mappedPhoto.OriginalFileName);
        Assert.Equal(120, mappedPhoto.Size);
        Assert.Equal("image/jpeg", mappedPhoto.ContentType);
        Assert.Equal(new[] { first.Id, second.Id }.OrderBy(id => id), bulkPhotos.Select(photo => photo.PhotoId).OrderBy(id => id));
        Assert.Empty(emptyBulk);
    }

    [Fact]
    public async Task OrganizationGalleryReadsRequireMembershipAndVehicleBelonging()
    {
        await using var context = fixture.CreateContext();
        var (owner, organization, vehicle) = await VehiclePhotoServiceTestSupport.SeedOrganizationVehicleAsync(context);
        var outsider = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName ="organization-outsider",
            Email = $"{Guid.NewGuid():N}@example.test"
        };
        context.Users.Add(outsider);
        context.VehiclePhotos.Add(VehiclePhotoServiceTestSupport.CreatePhoto(vehicle, "org.jpg", true));
        await context.SaveChangesAsync();
        using var services = new VehiclePhotoServiceTestSupport(context);

        var result = await services.Gallery.GetVehiclePhotosAsync(owner.Id, organization.Id, vehicle.Id);
        var accessError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            services.Gallery.GetVehiclePhotosAsync(outsider.Id, organization.Id, vehicle.Id));
        var mismatchError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            services.Gallery.GetVehiclePhotosAsync(owner.Id, Guid.NewGuid(), vehicle.Id));

        Assert.Single(result);
        Assert.Contains("доступа", accessError.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("доступа", mismatchError.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeletePrimaryPhotoChoosesOldestRemainingGalleryPhotoAndRefreshesVehiclePath()
    {
        await using var context = fixture.CreateContext();
        var (user, vehicle) = await VehiclePhotoServiceTestSupport.SeedPersonalVehicleAsync(context);
        var deletedPhoto = VehiclePhotoServiceTestSupport.CreatePhoto(vehicle, "primary.jpg", true, createdAt: DateTime.UtcNow.AddDays(-2));
        var remainingPhoto = VehiclePhotoServiceTestSupport.CreatePhoto(vehicle, "next.jpg", false, createdAt: DateTime.UtcNow.AddDays(-1));
        context.VehiclePhotos.AddRange(deletedPhoto, remainingPhoto);
        await context.SaveChangesAsync();
        using var services = new VehiclePhotoServiceTestSupport(context);
        Assert.Equal(deletedPhoto.Path, await services.Gallery.GetVehiclePhotoPathAsync(user.Id, vehicle.Id));

        await services.Gallery.DeleteVehiclePhotoAsync(user.Id, vehicle.Id);

        Assert.True((await context.VehiclePhotos.SingleAsync(photo => photo.Id == deletedPhoto.Id)).IsDeleted);
        Assert.True((await context.VehiclePhotos.SingleAsync(photo => photo.Id == remainingPhoto.Id)).IsPrimary);
        var history = await context.VehicleHistoryEvents.SingleAsync(item => item.VehicleId == vehicle.Id);
        Assert.Equal("vehicle_photo_deleted", history.EventType);
        Assert.Equal("Файл: primary.jpg", history.Description);
        Assert.Equal(remainingPhoto.Path, await services.Gallery.GetVehiclePhotoPathAsync(user.Id, vehicle.Id));
    }

    [Fact]
    public async Task LegacyDeleteUsesGalleryPhotoAndLeavesAlbumPhotoUntouchedOnFree()
    {
        await using var context = fixture.CreateContext();
        var (user, organization, vehicle) = await VehiclePhotoServiceTestSupport.SeedOrganizationVehicleAsync(context);
        var galleryPhoto = VehiclePhotoServiceTestSupport.CreatePhoto(vehicle, "gallery.jpg", true);
        var album = VehiclePhotoServiceTestSupport.CreateAlbum(vehicle);
        var albumPhoto = VehiclePhotoServiceTestSupport.CreatePhoto(vehicle, "album.jpg", true, album.Id);
        context.AddRange(galleryPhoto, album, albumPhoto);
        await context.SaveChangesAsync();
        var subscriptions = new Mock<OpenCRM.Services.Interfaces.Organizations.IOrganizationSubscriptionService>();
        subscriptions.Setup(service => service.HasFeatureAsync(user.Id, organization.Id, Helpers.FeatureCodes.VehiclePhotoAlbums))
            .ReturnsAsync(false);
        subscriptions.Setup(service => service.GetEffectivePlanAsync(user.Id, organization.Id))
            .ReturnsAsync(new SubscriptionPlanDto { IsDefaultPlan = true });
        using var services = new VehiclePhotoServiceTestSupport(context, organizationSubscriptions: subscriptions);

        await services.Gallery.DeleteVehiclePhotoAsync(user.Id, organization.Id, vehicle.Id);

        Assert.True((await context.VehiclePhotos.SingleAsync(photo => photo.Id == galleryPhoto.Id)).IsDeleted);
        Assert.False((await context.VehiclePhotos.SingleAsync(photo => photo.Id == albumPhoto.Id)).IsDeleted);
    }

    [Fact]
    public async Task LegacyDeleteRejectsAlbumPhotoWhenRegularGalleryIsEmptyOnFree()
    {
        await using var context = fixture.CreateContext();
        var (user, organization, vehicle) = await VehiclePhotoServiceTestSupport.SeedOrganizationVehicleAsync(context);
        var album = VehiclePhotoServiceTestSupport.CreateAlbum(vehicle);
        var albumPhoto = VehiclePhotoServiceTestSupport.CreatePhoto(vehicle, "album.jpg", true, album.Id);
        context.AddRange(album, albumPhoto);
        await context.SaveChangesAsync();
        var subscriptions = new Mock<OpenCRM.Services.Interfaces.Organizations.IOrganizationSubscriptionService>();
        subscriptions.Setup(service => service.HasFeatureAsync(user.Id, organization.Id, Helpers.FeatureCodes.VehiclePhotoAlbums))
            .ReturnsAsync(false);
        subscriptions.Setup(service => service.GetEffectivePlanAsync(user.Id, organization.Id))
            .ReturnsAsync(new SubscriptionPlanDto { IsDefaultPlan = true });
        using var services = new VehiclePhotoServiceTestSupport(context, organizationSubscriptions: subscriptions);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            services.Gallery.DeleteVehiclePhotoAsync(user.Id, organization.Id, vehicle.Id));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            services.Gallery.DeleteVehiclePhotoAsync(user.Id, organization.Id, vehicle.Id, albumPhoto.Id));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            services.Gallery.SetPrimaryVehiclePhotoAsync(user.Id, organization.Id, vehicle.Id, albumPhoto.Id));

        Assert.False((await context.VehiclePhotos.SingleAsync(photo => photo.Id == albumPhoto.Id)).IsDeleted);
        Assert.Empty(await context.VehicleHistoryEvents.Where(item => item.VehicleId == vehicle.Id).ToListAsync());
    }

    [Fact]
    public async Task DeleteLegacyVehiclePhotoRejectsVehicleWithoutPhotos()
    {
        await using var context = fixture.CreateContext();
        var (user, vehicle) = await VehiclePhotoServiceTestSupport.SeedPersonalVehicleAsync(context);
        using var services = new VehiclePhotoServiceTestSupport(context);

        await Assert.ThrowsAsync<ArgumentException>(() => services.Gallery.DeleteVehiclePhotoAsync(user.Id, vehicle.Id));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DeletingGalleryPhotoWithoutFallbackPreservesRemainingPrimaryState(bool deletePrimary)
    {
        await using var context = fixture.CreateContext();
        var (user, vehicle) = await VehiclePhotoServiceTestSupport.SeedPersonalVehicleAsync(context);
        var first = VehiclePhotoServiceTestSupport.CreatePhoto(vehicle, "first.jpg", true);
        var second = deletePrimary ? null : VehiclePhotoServiceTestSupport.CreatePhoto(vehicle, "second.jpg", false);
        context.VehiclePhotos.Add(first);
        if (second != null)
        {
            context.VehiclePhotos.Add(second);
        }

        await context.SaveChangesAsync();
        using var services = new VehiclePhotoServiceTestSupport(context);

        var deletedPhoto = deletePrimary ? first : second!;
        await services.Gallery.DeleteVehiclePhotoAsync(user.Id, null, vehicle.Id, deletedPhoto.Id);

        Assert.True(await context.VehiclePhotos.Where(photo => photo.Id == deletedPhoto.Id).Select(photo => photo.IsDeleted).SingleAsync());
        if (second != null)
        {
            Assert.True(await context.VehiclePhotos.Where(photo => photo.Id == first.Id).Select(photo => photo.IsPrimary).SingleAsync());
        }
        Assert.Single(await context.VehicleHistoryEvents.Where(item => item.VehicleId == vehicle.Id).ToListAsync());
    }

    [Fact]
    public async Task DeleteVehiclePhotoRejectsMissingAndForeignPhotoIds()
    {
        await using var context = fixture.CreateContext();
        var (owner, vehicle) = await VehiclePhotoServiceTestSupport.SeedPersonalVehicleAsync(context);
        var (other, _) = await VehiclePhotoServiceTestSupport.SeedPersonalVehicleAsync(context);
        var photo = VehiclePhotoServiceTestSupport.CreatePhoto(vehicle, "owner.jpg", true);
        context.VehiclePhotos.Add(photo);
        await context.SaveChangesAsync();
        using var services = new VehiclePhotoServiceTestSupport(context);

        await Assert.ThrowsAsync<ArgumentException>(() => services.Gallery.DeleteVehiclePhotoAsync(owner.Id, null, vehicle.Id, Guid.NewGuid()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => services.Gallery.DeleteVehiclePhotoAsync(other.Id, null, vehicle.Id, photo.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => services.Gallery.DeleteVehiclePhotoAsync(owner.Id, Guid.NewGuid(), vehicle.Id));

        Assert.False(photo.IsDeleted);
        Assert.Empty(await context.VehicleHistoryEvents.Where(item => item.VehicleId == vehicle.Id).ToListAsync());
    }

    [Fact]
    public async Task SetPrimaryGalleryPhotoRejectsForeignIdUpdatesRowsAndRefreshesCachedPath()
    {
        await using var context = fixture.CreateContext();
        var (user, vehicle) = await VehiclePhotoServiceTestSupport.SeedPersonalVehicleAsync(context);
        var first = VehiclePhotoServiceTestSupport.CreatePhoto(vehicle, "first.jpg", true);
        var second = VehiclePhotoServiceTestSupport.CreatePhoto(vehicle, "second.jpg", false);
        context.VehiclePhotos.AddRange(first, second);
        await context.SaveChangesAsync();
        using var services = new VehiclePhotoServiceTestSupport(context);
        Assert.Equal(first.Path, await services.Gallery.GetVehiclePhotoPathAsync(user.Id, vehicle.Id));

        await Assert.ThrowsAsync<ArgumentException>(() => services.Gallery.SetPrimaryVehiclePhotoAsync(user.Id, null, vehicle.Id, Guid.NewGuid()));
        await services.Gallery.SetPrimaryVehiclePhotoAsync(user.Id, null, vehicle.Id, second.Id);

        Assert.False(first.IsPrimary);
        Assert.True(second.IsPrimary);
        Assert.Equal(second.Path, await services.Gallery.GetVehiclePhotoPathAsync(user.Id, vehicle.Id));
    }

    [Fact]
    public async Task DeletingPersonalPhotoWithMismatchedOrganizationFailsWorkspaceGuard()
    {
        await using var context = fixture.CreateContext();
        var (user, vehicle) = await VehiclePhotoServiceTestSupport.SeedPersonalVehicleAsync(context);
        var photo = VehiclePhotoServiceTestSupport.CreatePhoto(vehicle, "personal.jpg", true);
        context.VehiclePhotos.Add(photo);
        await context.SaveChangesAsync();
        using var services = new VehiclePhotoServiceTestSupport(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            services.Gallery.DeleteVehiclePhotoAsync(user.Id, Guid.NewGuid(), vehicle.Id, photo.Id));

        Assert.False(photo.IsDeleted);
    }

    [Fact]
    public async Task VehiclePhotoPathReturnsNullForEmptyGalleryAndUploadInvalidatesThatCacheEntry()
    {
        await using var context = fixture.CreateContext();
        var (user, vehicle) = await VehiclePhotoServiceTestSupport.SeedPersonalVehicleAsync(context);
        using var services = new VehiclePhotoServiceTestSupport(context);

        Assert.Null(await services.Gallery.GetVehiclePhotoPathAsync(user.Id, vehicle.Id));
        using var stream = new MemoryStream([1]);
        var photoId = await services.Gallery.UploadVehiclePhotoAsync(user.Id, vehicle.Id, stream, "new.jpg");

        var expectedPath = await context.VehiclePhotos.Where(photo => photo.Id == photoId).Select(photo => photo.Path).SingleAsync();
        Assert.Equal(expectedPath, await services.Gallery.GetVehiclePhotoPathAsync(user.Id, vehicle.Id));
    }
}
