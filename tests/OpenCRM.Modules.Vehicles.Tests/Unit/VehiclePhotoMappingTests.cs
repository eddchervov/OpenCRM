using OpenCRM.Domain;
using OpenCRM.Modules.Vehicles.Photos;

namespace OpenCRM.Modules.Vehicles.Tests.Unit;

public sealed class VehiclePhotoMappingTests
{
    [Fact]
    public void AlbumExcludesDeletedPhotosAndOrdersPrimaryBeforeChronologicalPhotos()
    {
        var vehicleId = Guid.NewGuid();
        var createdAt = new DateTime(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc);
        var first = new VehiclePhoto { Id = Guid.NewGuid(), VehicleId = vehicleId, CreatedAt = createdAt };
        var primary = new VehiclePhoto
        {
            Id = Guid.NewGuid(),
            VehicleId = vehicleId,
            AlbumId = Guid.NewGuid(),
            IsPrimary = true,
            OriginalFileName = "Техника.jpg",
            Size = 123,
            ContentType = "image/jpeg",
            CreatedAt = createdAt.AddMinutes(2)
        };
        var second = new VehiclePhoto { Id = Guid.NewGuid(), VehicleId = vehicleId, CreatedAt = createdAt.AddMinutes(1) };
        var deleted = new VehiclePhoto { Id = Guid.NewGuid(), IsPrimary = true, IsDeleted = true };
        var album = new VehiclePhotoAlbum
        {
            Id = primary.AlbumId.Value,
            VehicleId = vehicleId,
            Title = "Приёмка",
            CreatedAt = createdAt,
            Photos = [second, deleted, primary, first]
        };

        var result = VehiclePhotoMapper.MapAlbum(album);

        Assert.Equal(album.Id, result.AlbumId);
        Assert.Equal(vehicleId, result.VehicleId);
        Assert.Equal(album.Title, result.Title);
        Assert.Equal(createdAt, result.CreatedAt);
        Assert.Equal(3, result.PhotoCount);
        Assert.Equal(primary.Id, result.PrimaryPhotoId);
        Assert.Equal(new[] { primary.Id, first.Id, second.Id }, result.Photos.Select(photo => photo.PhotoId));
        var mappedPrimary = result.Photos[0];
        Assert.Equal(vehicleId, mappedPrimary.VehicleId);
        Assert.Equal(album.Id, mappedPrimary.AlbumId);
        Assert.True(mappedPrimary.IsPrimary);
        Assert.Equal(primary.OriginalFileName, mappedPrimary.OriginalFileName);
        Assert.Equal(primary.Size, mappedPrimary.Size);
        Assert.Equal(primary.ContentType, mappedPrimary.ContentType);
        Assert.Equal(primary.CreatedAt, mappedPrimary.CreatedAt);
    }

    [Fact]
    public void AlbumWithoutPrimaryUsesOldestActivePhoto()
    {
        var first = new VehiclePhoto { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow.AddDays(-1) };
        var second = new VehiclePhoto { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };
        var album = new VehiclePhotoAlbum { Photos = [second, first] };

        var result = VehiclePhotoMapper.MapAlbum(album);

        Assert.Equal(first.Id, result.PrimaryPhotoId);
        Assert.Equal(2, result.PhotoCount);
        Assert.All(result.Photos, photo => Assert.False(photo.IsPrimary));
    }

    [Fact]
    public void AlbumWithOnlyDeletedPhotosHasNoPrimary()
    {
        var album = new VehiclePhotoAlbum
        {
            Photos = [new VehiclePhoto { IsPrimary = true, IsDeleted = true }]
        };

        var result = VehiclePhotoMapper.MapAlbum(album);

        Assert.Empty(result.Photos);
        Assert.Equal(0, result.PhotoCount);
        Assert.Null(result.PrimaryPhotoId);
    }

    [Theory]
    [InlineData(true, true, true, false)]
    [InlineData(true, false, true, true)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    [InlineData(false, true, true, true)]
    public void WorkspaceRequiresMatchingOrganizationOrPersonalOwnership(
        bool organizationWorkspace,
        bool vehicleInOrganization,
        bool sameOwner,
        bool denied)
    {
        var organizationId = Guid.NewGuid();
        var vehicle = new Vehicle
        {
            UserId = sameOwner ? "owner" : "other",
            OrganizationId = vehicleInOrganization ? organizationId : null
        };
        Guid? workspaceId = organizationWorkspace ? organizationId : null;

        if (denied)
        {
            Assert.Throws<InvalidOperationException>(() => VehiclePhotoWorkspace.EnsureAccess("owner", workspaceId, vehicle));
            return;
        }

        VehiclePhotoWorkspace.EnsureAccess("owner", workspaceId, vehicle);
    }
}
