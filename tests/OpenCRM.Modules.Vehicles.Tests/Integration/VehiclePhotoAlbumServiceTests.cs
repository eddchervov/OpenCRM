using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Vehicles.Contracts.Photos;
using OpenCRM.Services.Interfaces.Media.Dto;
using OpenCRM.Services.Interfaces.Subscriptions.Dtos;

namespace OpenCRM.Modules.Vehicles.Tests.Integration;

public sealed class VehiclePhotoAlbumServiceTests(VehicleDatabaseFixture fixture) : IClassFixture<VehicleDatabaseFixture>
{
    [Fact]
    public async Task NeutralAlbumsIgnorePlanRestrictionsAndRetainTechnicalLimitAndMembershipChecks()
    {
        await using var context = fixture.CreateContext();
        var (owner, organization, vehicle) = await VehiclePhotoServiceTestSupport.SeedOrganizationVehicleAsync(context);
        var albums = Enumerable.Range(0, 99).Select(index => VehiclePhotoServiceTestSupport.CreateAlbum(vehicle, $"Альбом {index}")).ToArray();
        context.VehiclePhotoAlbums.AddRange(albums);
        await context.SaveChangesAsync();
        var subscriptions = new Mock<OpenCRM.Services.Interfaces.Organizations.IOrganizationSubscriptionService>();
        SetupPhotoAlbumsFeature(subscriptions, true);
        using var services = new VehiclePhotoServiceTestSupport(context, organizationSubscriptions: subscriptions, commercialEnabled: false);

        var limit = await services.Albums.GetVehiclePhotoAlbumLimitAsync(owner.Id, organization.Id, vehicle.Id);

        Assert.Equal(100, limit.MaxCount);
        Assert.False(limit.IsLimitedByPlan);
        Assert.True(limit.CanCreateMore);
        await services.Albums.CreateVehiclePhotoAlbumAsync(owner.Id, organization.Id, vehicle.Id, "Сотый альбом");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            services.Albums.CreateVehiclePhotoAlbumAsync(owner.Id, organization.Id, vehicle.Id, "Лишний альбом"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            services.Albums.GetVehiclePhotoAlbumLimitAsync("outsider", organization.Id, vehicle.Id));
        subscriptions.Verify(service => service.GetEffectivePlanAsync(It.IsAny<string>(), It.IsAny<Guid>()), Times.Never);
        subscriptions.Verify(service => service.HasFeatureAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task AlbumQueriesReturnActiveAlbumsAndExcludeDeletedPhotosAndAlbums()
    {
        await using var context = fixture.CreateContext();
        var (user, vehicle) = await VehiclePhotoServiceTestSupport.SeedPersonalVehicleAsync(context);
        var album = VehiclePhotoServiceTestSupport.CreateAlbum(vehicle, "Папка с фото");
        var deletedAlbum = VehiclePhotoServiceTestSupport.CreateAlbum(vehicle, "Удаленная папка", true);
        var firstPhoto = VehiclePhotoServiceTestSupport.CreatePhoto(vehicle, "first.jpg", false, album.Id, DateTime.UtcNow.AddMinutes(-2));
        var secondPhoto = VehiclePhotoServiceTestSupport.CreatePhoto(vehicle, "second.jpg", false, album.Id, DateTime.UtcNow.AddMinutes(-1));
        var deletedPhoto = VehiclePhotoServiceTestSupport.CreatePhoto(vehicle, "deleted.jpg", false, album.Id);
        deletedPhoto.IsDeleted = true;
        var deletedAlbumPhoto = VehiclePhotoServiceTestSupport.CreatePhoto(vehicle, "deleted-album.jpg", false, deletedAlbum.Id);
        context.AddRange(album, deletedAlbum, firstPhoto, secondPhoto, deletedPhoto, deletedAlbumPhoto);
        await context.SaveChangesAsync();
        using var services = new VehiclePhotoServiceTestSupport(context);

        var albums = await services.Albums.GetVehiclePhotoAlbumsAsync(user.Id, null, vehicle.Id);
        var single = await services.Albums.GetVehiclePhotoAlbumAsync(user.Id, null, vehicle.Id, album.Id);

        var result = Assert.Single(albums);
        Assert.Equal(album.Id, result.AlbumId);
        Assert.Equal("Папка с фото", result.Title);
        Assert.Equal(2, result.PhotoCount);
        Assert.Equal(new[] { firstPhoto.Id, secondPhoto.Id }, result.Photos.Select(photo => photo.PhotoId));
        Assert.Equal(firstPhoto.Id, result.PrimaryPhotoId);
        Assert.Equal(result.AlbumId, single.AlbumId);
        Assert.Equal(2, single.Photos.Count);
        await Assert.ThrowsAsync<ArgumentException>(() => services.Albums.GetVehiclePhotoAlbumAsync(user.Id, null, vehicle.Id, deletedAlbum.Id));
    }

    [Fact]
    public async Task AlbumQueriesRejectMissingVehicleAndOrganizationOutsider()
    {
        await using var context = fixture.CreateContext();
        var (owner, organization, vehicle) = await VehiclePhotoServiceTestSupport.SeedOrganizationVehicleAsync(context);
        var outsider = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName ="album-outsider",
            Email = $"{Guid.NewGuid():N}@example.test"
        };
        context.Users.Add(outsider);
        await context.SaveChangesAsync();
        using var services = new VehiclePhotoServiceTestSupport(context);

        await Assert.ThrowsAsync<ArgumentException>(() => services.Albums.GetVehiclePhotoAlbumsAsync(owner.Id, organization.Id, Guid.NewGuid()));
        await Assert.ThrowsAsync<ArgumentException>(() => services.Albums.GetVehiclePhotoAlbumAsync(owner.Id, organization.Id, Guid.NewGuid(), Guid.NewGuid()));
        await Assert.ThrowsAsync<ArgumentException>(() => services.Albums.GetVehiclePhotoAlbumLimitAsync(owner.Id, organization.Id, Guid.NewGuid()));
        await Assert.ThrowsAsync<ArgumentException>(() => services.Albums.CreateVehiclePhotoAlbumAsync(owner.Id, organization.Id, Guid.NewGuid(), "Альбом"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => services.Albums.GetVehiclePhotoAlbumsAsync(outsider.Id, organization.Id, vehicle.Id));
        await Assert.ThrowsAsync<ArgumentException>(() => services.Albums.GetVehiclePhotoAlbumAsync(owner.Id, organization.Id, vehicle.Id, Guid.NewGuid()));
    }

    [Theory]
    [InlineData(false, false, false, 0, true)]
    [InlineData(true, true, false, 120, true)]
    [InlineData(true, false, true, 120, true)]
    [InlineData(true, false, false, 0, true)]
    [InlineData(true, false, false, 120, false)]
    public async Task OrganizationAlbumLimitUsesEffectivePlanClassification(
        bool hasPlan,
        bool isDefaultPlan,
        bool isDemoPlan,
        decimal effectivePrice,
        bool expectedLimited)
    {
        await using var context = fixture.CreateContext();
        var (user, organization, vehicle) = await VehiclePhotoServiceTestSupport.SeedOrganizationVehicleAsync(context);
        var subscriptions = new Mock<OpenCRM.Services.Interfaces.Organizations.IOrganizationSubscriptionService>();
        SetupPhotoAlbumsFeature(subscriptions, true);
        if (hasPlan)
        {
            subscriptions.Setup(service => service.GetEffectivePlanAsync(user.Id, organization.Id))
                .ReturnsAsync(new SubscriptionPlanDto
                {
                    IsDefaultPlan = isDefaultPlan,
                    IsDemoPlan = isDemoPlan,
                    EffectivePrice = effectivePrice
                });
        }
        else
        {
            subscriptions.Setup(service => service.GetEffectivePlanAsync(user.Id, organization.Id))
                .ReturnsAsync((SubscriptionPlanDto?)null);
        }

        using var services = new VehiclePhotoServiceTestSupport(context, organizationSubscriptions: subscriptions);

        var result = await services.Albums.GetVehiclePhotoAlbumLimitAsync(user.Id, organization.Id, vehicle.Id);

        Assert.Equal(expectedLimited, result.IsLimitedByPlan);
        Assert.Equal(expectedLimited ? 1 : 100, result.MaxCount);
        Assert.True(result.CanCreateMore);
    }

    [Fact]
    public async Task PersonalAlbumLimitUsesDefaultFallbackForFreeAndActiveSubscriptionForPaidPlan()
    {
        await using var freeContext = fixture.CreateContext();
        var (freeUser, freeVehicle) = await VehiclePhotoServiceTestSupport.SeedPersonalVehicleAsync(freeContext);
        using var freeServices = new VehiclePhotoServiceTestSupport(freeContext);

        var freeLimit = await freeServices.Albums.GetVehiclePhotoAlbumLimitAsync(freeUser.Id, null, freeVehicle.Id);

        Assert.True(freeLimit.IsLimitedByPlan);
        Assert.Equal(1, freeLimit.MaxCount);

        await using var paidContext = fixture.CreateContext();
        var (paidUser, paidVehicle) = await VehiclePhotoServiceTestSupport.SeedPersonalVehicleAsync(paidContext);
        var paidPlan = new SubscriptionPlan
        {
            Name = "Фото Плюс",
            Description = "Платный план для теста",
            Price = 500,
            DurationDays = 30,
            PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Personal
        };
        paidContext.SubscriptionPlans.Add(paidPlan);
        await paidContext.SaveChangesAsync();
        paidContext.UserSubscriptions.Add(new UserSubscription
        {
            UserId = paidUser.Id,
            SubscriptionPlanId = paidPlan.Id,
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(10)
        });
        await paidContext.SaveChangesAsync();
        using var paidServices = new VehiclePhotoServiceTestSupport(paidContext);

        var paidLimit = await paidServices.Albums.GetVehiclePhotoAlbumLimitAsync(paidUser.Id, null, paidVehicle.Id);

        Assert.False(paidLimit.IsLimitedByPlan);
        Assert.Equal(100, paidLimit.MaxCount);
    }

    [Fact]
    public async Task AlbumCreationNormalizesBlankAndTrimmedTitlesAndEnforcesFreeLimit()
    {
        await using var context = fixture.CreateContext();
        var (user, vehicle) = await VehiclePhotoServiceTestSupport.SeedPersonalVehicleAsync(context);
        using var services = new VehiclePhotoServiceTestSupport(context);

        var album = await services.Albums.CreateVehiclePhotoAlbumAsync(user.Id, null, vehicle.Id, "   ");

        Assert.Equal("Новый альбом", album.Title);
        Assert.Equal(0, album.PhotoCount);
        var limit = await services.Albums.GetVehiclePhotoAlbumLimitAsync(user.Id, null, vehicle.Id);
        Assert.Equal(1, limit.CurrentCount);
        Assert.False(limit.CanCreateMore);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            services.Albums.CreateVehiclePhotoAlbumAsync(user.Id, null, vehicle.Id, "Вторая папка"));
        Assert.Equal(1, await context.VehiclePhotoAlbums.CountAsync(item => item.VehicleId == vehicle.Id && !item.IsDeleted));
    }

    [Fact]
    public async Task AlbumCreationTrimsTitleAndAcceptsMaximumLength()
    {
        await using var context = fixture.CreateContext();
        var (user, organization, vehicle) = await VehiclePhotoServiceTestSupport.SeedOrganizationVehicleAsync(context);
        var subscriptions = new Mock<OpenCRM.Services.Interfaces.Organizations.IOrganizationSubscriptionService>();
        SetupPhotoAlbumsFeature(subscriptions, true);
        subscriptions.Setup(service => service.GetEffectivePlanAsync(user.Id, It.IsAny<Guid>()))
            .ReturnsAsync(new SubscriptionPlanDto { EffectivePrice = 10 });
        using var services = new VehiclePhotoServiceTestSupport(context, organizationSubscriptions: subscriptions);

        var trimmed = await services.Albums.CreateVehiclePhotoAlbumAsync(user.Id, vehicle.OrganizationId, vehicle.Id, "  рабочая зона  ");
        var maxLengthTitle = new string('А', 120);
        var maximum = await services.Albums.CreateVehiclePhotoAlbumAsync(user.Id, vehicle.OrganizationId, vehicle.Id, maxLengthTitle);

        Assert.Equal("рабочая зона", trimmed.Title);
        Assert.Equal(120, maximum.Title.Length);
        Assert.Equal(maxLengthTitle, maximum.Title);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("title>")]
    [InlineData("javascript:alert(1)")]
    [InlineData("длинное название")]
    public async Task AlbumCreationRejectsMarkupScriptsAndOverlongTitles(string invalidTitle)
    {
        await using var context = fixture.CreateContext();
        var (user, organization, vehicle) = await VehiclePhotoServiceTestSupport.SeedOrganizationVehicleAsync(context);
        var subscriptions = new Mock<OpenCRM.Services.Interfaces.Organizations.IOrganizationSubscriptionService>();
        SetupPhotoAlbumsFeature(subscriptions, true);
        subscriptions.Setup(service => service.GetEffectivePlanAsync(user.Id, organization.Id))
            .ReturnsAsync(new SubscriptionPlanDto { EffectivePrice = 10 });
        using var services = new VehiclePhotoServiceTestSupport(context, organizationSubscriptions: subscriptions);
        if (invalidTitle == "длинное название")
        {
            invalidTitle = new string('д', 121);
        }

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            services.Albums.CreateVehiclePhotoAlbumAsync(user.Id, organization.Id, vehicle.Id, invalidTitle));

        Assert.Contains(invalidTitle.Length > 120 ? "120" : "Название альбома", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await context.VehiclePhotoAlbums.Where(item => item.VehicleId == vehicle.Id).ToListAsync());
    }

    [Fact]
    public async Task PaidOrganizationCanCreateOneHundredthAlbumButNotOneHundredFirst()
    {
        await using var context = fixture.CreateContext();
        var (user, organization, vehicle) = await VehiclePhotoServiceTestSupport.SeedOrganizationVehicleAsync(context);
        context.VehiclePhotoAlbums.AddRange(Enumerable.Range(0, 99)
            .Select(index => VehiclePhotoServiceTestSupport.CreateAlbum(vehicle, $"Album {index}")));
        await context.SaveChangesAsync();
        var subscriptions = new Mock<OpenCRM.Services.Interfaces.Organizations.IOrganizationSubscriptionService>();
        SetupPhotoAlbumsFeature(subscriptions, true);
        subscriptions.Setup(service => service.GetEffectivePlanAsync(user.Id, organization.Id))
            .ReturnsAsync(new SubscriptionPlanDto { EffectivePrice = 10 });
        using var services = new VehiclePhotoServiceTestSupport(context, organizationSubscriptions: subscriptions);

        var limit = await services.Albums.GetVehiclePhotoAlbumLimitAsync(user.Id, organization.Id, vehicle.Id);
        var hundredth = await services.Albums.CreateVehiclePhotoAlbumAsync(user.Id, organization.Id, vehicle.Id, "Сотый альбом");

        Assert.Equal(99, limit.CurrentCount);
        Assert.Equal(100, limit.MaxCount);
        Assert.Equal("Сотый альбом", hundredth.Title);
        var createError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            services.Albums.CreateVehiclePhotoAlbumAsync(user.Id, organization.Id, vehicle.Id, "Сто первый альбом"));
        Assert.Contains("100", createError.Message);
        Assert.Equal(100, await context.VehiclePhotoAlbums.CountAsync(item => item.VehicleId == vehicle.Id && !item.IsDeleted));
    }

    [Fact]
    public async Task UploadingAlbumPhotosSetsFirstPrimaryAndUsesAlbumStorageFolder()
    {
        await using var context = fixture.CreateContext();
        var (user, vehicle) = await VehiclePhotoServiceTestSupport.SeedPersonalVehicleAsync(context);
        var album = VehiclePhotoServiceTestSupport.CreateAlbum(vehicle);
        var deletedPrimary = VehiclePhotoServiceTestSupport.CreatePhoto(vehicle, "deleted-primary.jpg", true, album.Id);
        deletedPrimary.IsDeleted = true;
        context.AddRange(album, deletedPrimary);
        await context.SaveChangesAsync();
        using var services = new VehiclePhotoServiceTestSupport(context);
        using var firstStream = new MemoryStream([1, 2]);
        using var secondStream = new MemoryStream([3]);

        var ids = await services.Albums.UploadVehicleAlbumPhotosAsync(user.Id, null, vehicle.Id, album.Id,
        [
            (firstStream, "one.jpg"),
            (secondStream, "two.jpg")
        ]);

        var photos = await context.VehiclePhotos
            .Where(photo => photo.AlbumId == album.Id && !photo.IsDeleted)
            .ToDictionaryAsync(photo => photo.Id);
        Assert.Equal(ids.Order(), photos.Keys.Order());
        Assert.True(photos[ids[0]].IsPrimary);
        Assert.False(photos[ids[1]].IsPrimary);
        Assert.Equal(album.Id, photos[ids[0]].AlbumId);
        services.ImageProcessing.Verify(processor => processor.ProcessAndSaveImageAsync(
            It.IsAny<Stream>(), It.Is<ImageProcessingOptions>(options => options.TargetFolder == $"img/users/{user.Id}/vehicles/albums/{album.Id:D}"
                && options.MaxWidth == 1200 && options.MaxHeight == 800 && options.Quality == 70)), Times.Exactly(2));

        using var nextStream = new MemoryStream([4]);
        var nextId = Assert.Single(await services.Albums.UploadVehicleAlbumPhotosAsync(user.Id, null, vehicle.Id, album.Id, [(nextStream, "three.jpg")]));
        Assert.False(await context.VehiclePhotos.Where(photo => photo.Id == nextId).Select(photo => photo.IsPrimary).SingleAsync());
    }

    [Fact]
    public async Task UploadAlbumPhotosRejectsEmptyFilesAndMissingOrForeignAlbums()
    {
        await using var context = fixture.CreateContext();
        var (user, vehicle) = await VehiclePhotoServiceTestSupport.SeedPersonalVehicleAsync(context);
        var (otherUser, _) = await VehiclePhotoServiceTestSupport.SeedPersonalVehicleAsync(context);
        var album = VehiclePhotoServiceTestSupport.CreateAlbum(vehicle);
        context.VehiclePhotoAlbums.Add(album);
        await context.SaveChangesAsync();
        using var services = new VehiclePhotoServiceTestSupport(context);

        await Assert.ThrowsAsync<ArgumentException>(() => services.Albums.UploadVehicleAlbumPhotosAsync(user.Id, null, vehicle.Id, Guid.NewGuid(), []));
        await Assert.ThrowsAsync<InvalidOperationException>(() => services.Albums.UploadVehicleAlbumPhotosAsync(otherUser.Id, null, vehicle.Id, album.Id, []));
        var emptyError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            services.Albums.UploadVehicleAlbumPhotosAsync(user.Id, null, vehicle.Id, album.Id, []));

        Assert.Contains("Выберите фотографии", emptyError.Message);
        services.ImageProcessing.Verify(processor => processor.ProcessAndSaveImageAsync(It.IsAny<Stream>(), It.IsAny<ImageProcessingOptions>()), Times.Never);
    }

    [Fact]
    public async Task PartialAlbumStorageFailurePersistsNoPhotoRows()
    {
        await using var context = fixture.CreateContext();
        var (user, vehicle) = await VehiclePhotoServiceTestSupport.SeedPersonalVehicleAsync(context);
        var album = VehiclePhotoServiceTestSupport.CreateAlbum(vehicle);
        context.VehiclePhotoAlbums.Add(album);
        await context.SaveChangesAsync();
        var imageProcessing = VehiclePhotoServiceTestSupport.CreateImageProcessingMock();
        imageProcessing.SetupSequence(processor => processor.ProcessAndSaveImageAsync(It.IsAny<Stream>(), It.IsAny<ImageProcessingOptions>()))
            .ReturnsAsync("img/shared/album-first.jpg")
            .ThrowsAsync(new IOException("Ошибка хранилища при обработке второго фото."));
        using var services = new VehiclePhotoServiceTestSupport(context, imageProcessing);
        using var firstStream = new MemoryStream([1]);
        using var secondStream = new MemoryStream([2]);

        await Assert.ThrowsAsync<IOException>(() => services.Albums.UploadVehicleAlbumPhotosAsync(user.Id, null, vehicle.Id, album.Id,
        [
            (firstStream, "first.jpg"),
            (secondStream, "second.jpg")
        ]));

        Assert.Empty(await context.VehiclePhotos.Where(photo => photo.AlbumId == album.Id).ToListAsync());
        imageProcessing.Verify(processor => processor.ProcessAndSaveImageAsync(It.IsAny<Stream>(), It.IsAny<ImageProcessingOptions>()), Times.Exactly(2));
    }

    [Fact]
    public async Task DeletingAlbumSoftDeletesAlbumAndLivePhotos()
    {
        await using var context = fixture.CreateContext();
        var (user, vehicle) = await VehiclePhotoServiceTestSupport.SeedPersonalVehicleAsync(context);
        var album = VehiclePhotoServiceTestSupport.CreateAlbum(vehicle);
        var livePhoto = VehiclePhotoServiceTestSupport.CreatePhoto(vehicle, "live.jpg", true, album.Id);
        var alreadyDeletedPhoto = VehiclePhotoServiceTestSupport.CreatePhoto(vehicle, "deleted.jpg", false, album.Id);
        alreadyDeletedPhoto.IsDeleted = true;
        context.AddRange(album, livePhoto, alreadyDeletedPhoto);
        await context.SaveChangesAsync();
        using var services = new VehiclePhotoServiceTestSupport(context);
        await services.Albums.DeleteVehiclePhotoAlbumAsync(user.Id, null, vehicle.Id, album.Id);

        Assert.True((await context.VehiclePhotoAlbums.SingleAsync(item => item.Id == album.Id)).IsDeleted);
        Assert.True((await context.VehiclePhotos.SingleAsync(photo => photo.Id == livePhoto.Id)).IsDeleted);
        Assert.True((await context.VehiclePhotos.SingleAsync(photo => photo.Id == alreadyDeletedPhoto.Id)).IsDeleted);
        await Assert.ThrowsAsync<ArgumentException>(() => services.Albums.GetVehiclePhotoAlbumAsync(user.Id, null, vehicle.Id, album.Id));
        Assert.Empty(await context.VehicleHistoryEvents.Where(item => item.VehicleId == vehicle.Id).ToListAsync());
    }

    [Fact]
    public async Task DeletingAlbumPhotoChoosesOldestLiveFallback()
    {
        await using var context = fixture.CreateContext();
        var (user, vehicle) = await VehiclePhotoServiceTestSupport.SeedPersonalVehicleAsync(context);
        var album = VehiclePhotoServiceTestSupport.CreateAlbum(vehicle);
        var deletedPhoto = VehiclePhotoServiceTestSupport.CreatePhoto(vehicle, "primary.jpg", true, album.Id);
        var remainingPhoto = VehiclePhotoServiceTestSupport.CreatePhoto(vehicle, "next.jpg", false, album.Id);
        context.AddRange(album, deletedPhoto, remainingPhoto);
        await context.SaveChangesAsync();
        using var services = new VehiclePhotoServiceTestSupport(context);
        await services.Albums.DeleteVehicleAlbumPhotoAsync(user.Id, null, vehicle.Id, album.Id, deletedPhoto.Id);

        Assert.True(deletedPhoto.IsDeleted);
        Assert.True(remainingPhoto.IsPrimary);
    }

    [Fact]
    public async Task DeletingNonPrimaryAlbumPhotoKeepsCurrentPrimaryAndMissingPhotoIsRejected()
    {
        await using var context = fixture.CreateContext();
        var (user, vehicle) = await VehiclePhotoServiceTestSupport.SeedPersonalVehicleAsync(context);
        var album = VehiclePhotoServiceTestSupport.CreateAlbum(vehicle);
        var primary = VehiclePhotoServiceTestSupport.CreatePhoto(vehicle, "primary.jpg", true, album.Id);
        var secondary = VehiclePhotoServiceTestSupport.CreatePhoto(vehicle, "secondary.jpg", false, album.Id);
        context.AddRange(album, primary, secondary);
        await context.SaveChangesAsync();
        using var services = new VehiclePhotoServiceTestSupport(context);

        await services.Albums.DeleteVehicleAlbumPhotoAsync(user.Id, null, vehicle.Id, album.Id, secondary.Id);
        await Assert.ThrowsAsync<ArgumentException>(() => services.Albums.DeleteVehicleAlbumPhotoAsync(
            user.Id, null, vehicle.Id, album.Id, Guid.NewGuid()));

        Assert.True(primary.IsPrimary);
        Assert.True(secondary.IsDeleted);
    }

    [Fact]
    public async Task AlbumPhotoMutationRejectsPhotoFromAnotherAlbumAndForeignWorkspace()
    {
        await using var context = fixture.CreateContext();
        var (user, vehicle) = await VehiclePhotoServiceTestSupport.SeedPersonalVehicleAsync(context);
        var (otherUser, _) = await VehiclePhotoServiceTestSupport.SeedPersonalVehicleAsync(context);
        var album = VehiclePhotoServiceTestSupport.CreateAlbum(vehicle);
        var otherAlbum = VehiclePhotoServiceTestSupport.CreateAlbum(vehicle, "Другой альбом");
        var photo = VehiclePhotoServiceTestSupport.CreatePhoto(vehicle, "owned.jpg", true, otherAlbum.Id);
        context.AddRange(album, otherAlbum, photo);
        await context.SaveChangesAsync();
        using var services = new VehiclePhotoServiceTestSupport(context);

        await Assert.ThrowsAsync<ArgumentException>(() => services.Albums.SetPrimaryVehicleAlbumPhotoAsync(user.Id, null, vehicle.Id, album.Id, photo.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => services.Albums.DeleteVehicleAlbumPhotoAsync(otherUser.Id, null, vehicle.Id, otherAlbum.Id, photo.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => services.Albums.DeleteVehiclePhotoAlbumAsync(user.Id, Guid.NewGuid(), vehicle.Id, otherAlbum.Id));
        Assert.False(photo.IsDeleted);
    }

    [Fact]
    public async Task SetPrimaryAlbumPhotoUpdatesAlbumPhotosAndRejectsUnknownPhoto()
    {
        await using var context = fixture.CreateContext();
        var (user, vehicle) = await VehiclePhotoServiceTestSupport.SeedPersonalVehicleAsync(context);
        var album = VehiclePhotoServiceTestSupport.CreateAlbum(vehicle);
        var first = VehiclePhotoServiceTestSupport.CreatePhoto(vehicle, "first.jpg", true, album.Id);
        var second = VehiclePhotoServiceTestSupport.CreatePhoto(vehicle, "second.jpg", false, album.Id);
        context.AddRange(album, first, second);
        await context.SaveChangesAsync();
        using var services = new VehiclePhotoServiceTestSupport(context);

        await Assert.ThrowsAsync<ArgumentException>(() => services.Albums.SetPrimaryVehicleAlbumPhotoAsync(user.Id, null, vehicle.Id, album.Id, Guid.NewGuid()));
        await services.Albums.SetPrimaryVehicleAlbumPhotoAsync(user.Id, null, vehicle.Id, album.Id, second.Id);

        Assert.False(first.IsPrimary);
        Assert.True(second.IsPrimary);
    }

    [Fact]
    public async Task AlbumPhotoOperationsRejectOrganizationMismatch()
    {
        await using var context = fixture.CreateContext();
        var (user, organization, vehicle) = await VehiclePhotoServiceTestSupport.SeedOrganizationVehicleAsync(context);
        var album = VehiclePhotoServiceTestSupport.CreateAlbum(vehicle);
        var photo = VehiclePhotoServiceTestSupport.CreatePhoto(vehicle, "photo.jpg", true, album.Id);
        context.AddRange(album, photo);
        await context.SaveChangesAsync();
        using var services = new VehiclePhotoServiceTestSupport(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => services.Albums.SetPrimaryVehicleAlbumPhotoAsync(
            user.Id, Guid.NewGuid(), vehicle.Id, album.Id, photo.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => services.Albums.DeleteVehicleAlbumPhotoAsync(
            user.Id, null, vehicle.Id, album.Id, photo.Id));
        Assert.False(photo.IsDeleted);
        Assert.Equal(organization.Id, vehicle.OrganizationId);
    }

    [Fact]
    public async Task FreeOrganizationCannotCreatePhotoAlbumWithoutProFeature()
    {
        await using var context = fixture.CreateContext();
        var (user, organization, vehicle) = await VehiclePhotoServiceTestSupport.SeedOrganizationVehicleAsync(context);
        var subscriptions = new Mock<OpenCRM.Services.Interfaces.Organizations.IOrganizationSubscriptionService>();
        SetupPhotoAlbumsFeature(subscriptions, false);
        subscriptions.Setup(service => service.GetEffectivePlanAsync(user.Id, organization.Id))
            .ReturnsAsync(new SubscriptionPlanDto { IsDefaultPlan = true });
        using var services = new VehiclePhotoServiceTestSupport(context, organizationSubscriptions: subscriptions);

        var limit = await services.Albums.GetVehiclePhotoAlbumLimitAsync(user.Id, organization.Id, vehicle.Id);

        Assert.Equal(0, limit.MaxCount);
        Assert.False(limit.CanCreateMore);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            services.Albums.CreateVehiclePhotoAlbumAsync(user.Id, organization.Id, vehicle.Id, "Free альбом"));
        Assert.Empty(await context.VehiclePhotoAlbums.Where(album => album.VehicleId == vehicle.Id).ToListAsync());
    }

    private static void SetupPhotoAlbumsFeature(
        Mock<OpenCRM.Services.Interfaces.Organizations.IOrganizationSubscriptionService> subscriptions,
        bool isEnabled)
    {
        subscriptions.Setup(service => service.HasFeatureAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), Helpers.FeatureCodes.VehiclePhotoAlbums))
            .ReturnsAsync(isEnabled);
    }
}
