using OpenCRM.Modules.Billing.Contracts;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Domain.Interfaces.Media;
using OpenCRM.Domain.Interfaces.Vehicles;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.Repositories;
using OpenCRM.Infrastructure.FileStorage;
using OpenCRM.Modules.Vehicles.Contracts.Photos;
using OpenCRM.Modules.Vehicles.Photos;
using Helpers;
using OpenCRM.Services.Interfaces.Media;
using OpenCRM.Services.Interfaces.Media.Dto;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Subscriptions.Dtos;

namespace OpenCRM.Modules.Vehicles.Tests.Integration;

internal sealed class VehiclePhotoServiceTestSupport : IDisposable
{
    private readonly ServiceProvider _serviceProvider;

    public VehiclePhotoServiceTestSupport(
        OpenCRMContext context,
        Mock<IImageProcessingService>? imageProcessing = null,
        Mock<IOrganizationSubscriptionService>? organizationSubscriptions = null,
        bool commercialEnabled = true)
    {
        this.Context = context;
        this.ImageProcessing = imageProcessing ?? VehiclePhotoServiceTestSupport.CreateImageProcessingMock();
        this.OrganizationSubscriptions = organizationSubscriptions ?? new Mock<IOrganizationSubscriptionService>();
        if (organizationSubscriptions == null)
        {
            this.OrganizationSubscriptions
                .Setup(service => service.HasFeatureAsync(It.IsAny<string>(), It.IsAny<Guid>(), FeatureCodes.VehiclePhotoAlbums))
                .ReturnsAsync(true);
        }

        this.OrganizationSubscriptions
            .Setup(service => service.EnsureWorkspaceCanReadAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        this.OrganizationSubscriptions
            .Setup(service => service.EnsureWorkspaceCanWriteAsync(It.IsAny<string>(), It.IsAny<Guid>()))
            .Returns(Task.CompletedTask);
        this.OrganizationSubscriptions
            .Setup(service => service.EnsureWorkspaceCanWriteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        if (organizationSubscriptions == null)
        {
            this.OrganizationSubscriptions
                .Setup(service => service.GetEffectivePlanAsync(It.IsAny<string>(), It.IsAny<Guid>()))
                .ReturnsAsync((SubscriptionPlanDto?)null);
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddSingleton(context);
        services.AddSingleton<IOrganizationSubscriptionService>(this.OrganizationSubscriptions.Object);
        services.AddOpenCRMFileStorageInfrastructure();
        services.AddSingleton<IImageProcessingService>(this.ImageProcessing.Object);
        this._serviceProvider = services.BuildServiceProvider();

        this.Cache = this._serviceProvider.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
        this.WorkspaceAccess = this._serviceProvider.GetRequiredService<IMediaWorkspaceAccess>();
        this.VehicleRepository = new VehicleRepository(context);
        this.VehiclePhotoRepository = new VehiclePhotoRepository(context, NullLogger<VehiclePhotoRepository>.Instance);
        var clock = new Mock<ISystemClock>();
        clock.SetupGet(systemClock => systemClock.UtcNow).Returns(new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc));
        clock.SetupGet(systemClock => systemClock.UtcToday).Returns(new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc));
        this.HistoryWriter = new VehicleHistoryWriter(
            context,
            clock.Object,
            this.VehicleRepository,
            this.OrganizationSubscriptions.Object);
        this.Gallery = new VehicleGalleryService(
            this.VehiclePhotoRepository,
            this.ImageProcessing.Object,
            this.VehicleRepository,
            this.Cache,
            context,
            this.HistoryWriter,
            this.WorkspaceAccess);
        this.Albums = new VehiclePhotoAlbumService(
            this.ImageProcessing.Object,
            this.VehicleRepository,
            context,
            this.OrganizationSubscriptions.Object,
            this.WorkspaceAccess,
            Mock.Of<IPlatformCommercialPolicy>(policy => policy.CommercialEnabled == commercialEnabled));
    }

    public OpenCRMContext Context { get; }

    public Mock<IImageProcessingService> ImageProcessing { get; }

    public Mock<IOrganizationSubscriptionService> OrganizationSubscriptions { get; }

    public Microsoft.Extensions.Caching.Memory.IMemoryCache Cache { get; }

    public IMediaWorkspaceAccess WorkspaceAccess { get; }

    public IVehicleRepository VehicleRepository { get; }

    public IVehiclePhotoRepository VehiclePhotoRepository { get; }

    public VehicleHistoryWriter HistoryWriter { get; }

    public IVehicleGalleryService Gallery { get; }

    public IVehiclePhotoAlbumService Albums { get; }

    public void Dispose()
    {
        this._serviceProvider.Dispose();
    }

    public static Mock<IImageProcessingService> CreateImageProcessingMock()
    {
        var imageProcessing = new Mock<IImageProcessingService>(MockBehavior.Strict);
        imageProcessing
            .Setup(service => service.ProcessAndSaveImageAsync(It.IsAny<Stream>(), It.IsAny<ImageProcessingOptions>()))
            .ReturnsAsync(() => $"img/vehicles/{Guid.NewGuid():N}.jpg");
        imageProcessing
            .Setup(service => service.GetImageFormatAsync(It.IsAny<Stream>()))
            .ReturnsAsync("image/jpeg");

        return imageProcessing;
    }

    public static async Task<(ApplicationUser User, Vehicle Vehicle)> SeedPersonalVehicleAsync(
        OpenCRMContext context,
        string? userId = null)
    {
        var user = new ApplicationUser
        {
            Id = userId ?? Guid.NewGuid().ToString(),
            UserName = $"user-{Guid.NewGuid():N}",
            Email = $"{Guid.NewGuid():N}@example.test"
        };
        var vehicle = new Vehicle
        {
            UserId = user.Id,
            User = user,
            DateTimeCreation = DateTime.UtcNow
        };
        context.Users.Add(user);
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();

        return (user, vehicle);
    }

    public static async Task<(ApplicationUser User, Organization Organization, Vehicle Vehicle)> SeedOrganizationVehicleAsync(
        OpenCRMContext context,
        string? userId = null)
    {
        var user = new ApplicationUser
        {
            Id = userId ?? Guid.NewGuid().ToString(),
            UserName = $"owner-{Guid.NewGuid():N}",
            Email = $"{Guid.NewGuid():N}@example.test"
        };
        var organization = new Organization
        {
            Name = "Организация фототестов",
            OwnerUser = user,
            OwnerUserId = user.Id
        };
        organization.Members.Add(new OrganizationMember
        {
            Organization = organization,
            OrganizationId = organization.Id,
            User = user,
            UserId = user.Id,
            RoleCode = OrganizationRoleCodes.Owner
        });
        var vehicle = new Vehicle
        {
            UserId = user.Id,
            User = user,
            OrganizationId = organization.Id,
            Organization = organization,
            DateTimeCreation = DateTime.UtcNow
        };
        context.Users.Add(user);
        context.Organizations.Add(organization);
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();

        return (user, organization, vehicle);
    }

    public static VehiclePhoto CreatePhoto(Vehicle vehicle, string path, bool isPrimary, Guid? albumId = null, DateTime? createdAt = null)
    {
        return new VehiclePhoto
        {
            Id = Guid.NewGuid(),
            Vehicle = vehicle,
            VehicleId = vehicle.Id,
            UserId = vehicle.UserId,
            Path = path,
            Type = OpenCRM.Domain.Enums.MediaType.VehiclePhoto,
            CreatedAt = createdAt ?? DateTime.UtcNow,
            Size = 120,
            ContentType = "image/jpeg",
            OriginalFileName = Path.GetFileName(path),
            IsPrimary = isPrimary,
            AlbumId = albumId
        };
    }

    public static VehiclePhotoAlbum CreateAlbum(Vehicle vehicle, string title = "Тестовый альбом", bool isDeleted = false)
    {
        return new VehiclePhotoAlbum
        {
            Id = Guid.NewGuid(),
            Vehicle = vehicle,
            VehicleId = vehicle.Id,
            UserId = vehicle.UserId,
            Title = title,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = isDeleted
        };
    }

    public sealed class FailOnSaveInterceptor(int saveNumber) : SaveChangesInterceptor
    {
        private int _saveCount;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref this._saveCount) == saveNumber)
            {
                throw new InvalidOperationException("Искусственный отказ сохранения для теста.");
            }

            return ValueTask.FromResult(result);
        }
    }
}
