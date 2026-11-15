using OpenCRM.Testing;
using Helpers;
using OpenCRM.Modules.Documents.Pdf;
using OpenCRM.Services.Interfaces.Pdf.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Modules.Maintenance;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.Repositories;
using OpenCRM.Infrastructure.FileStorage;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Modules.Maintenance.Photos;
using OpenCRM.Services.Interfaces.Media;
using OpenCRM.Services.Interfaces.Media.Dto;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Maintenance.Tests.Integration;

public sealed class MaintenanceIntakeTests(MaintenanceDatabaseFixture fixture) : IClassFixture<MaintenanceDatabaseFixture>
{
    [Fact]
    public async Task IntakePreservesStatusAndFirstTimestampAndRecordsAmendments()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceIntakeTests.CreateWork();
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        var clock = new Mock<ISystemClock>();
        var firstTime = new DateTime(2026, 9, 13, 10, 0, 0, DateTimeKind.Utc);
        clock.SetupGet(value => value.UtcNow).Returns(firstTime);
        var service = MaintenanceIntakeTests.CreateService(context, clock.Object);
        var userId = work.User.Id;
        var organizationId = work.Vehicle.Organization!.Id;

        await service.SaveIntake(userId, organizationId, work.Id, new SaveMaintenanceIntakeDto { Notes = "  Царапина на двери  " });
        clock.SetupGet(value => value.UtcNow).Returns(firstTime.AddHours(2));
        await service.SaveIntake(userId, organizationId, work.Id, new SaveMaintenanceIntakeDto { Notes = "Царапина на двери слева" });
        await service.SaveIntake(userId, organizationId, work.Id, new SaveMaintenanceIntakeDto { Notes = "Царапина на двери слева" });
        context.ChangeTracker.Clear();

        var saved = await context.Maintenance.SingleAsync(value => value.Id == work.Id);
        Assert.Equal(MaintenanceStatus.Planned, saved.Status);
        Assert.Null(saved.CompletedDate);
        Assert.Equal(firstTime, saved.IntakeRecordedAt);
        Assert.Equal(MaintenanceIntakeDiagramVersions.Current, saved.IntakeDiagramVersion);
        Assert.Equal("Царапина на двери слева", saved.IntakeNotes);
        var history = await context.MaintenanceHistoryEvents.Where(value => value.MaintenanceId == work.Id)
            .OrderBy(value => value.CreatedAt).ToListAsync();
        Assert.Equal(2, history.Count);
        Assert.Equal("Приёмка зафиксирована", history[0].Title);
        Assert.Contains("Было: Царапина на двери", history[1].Description);
        Assert.Equal(userId, history[1].UserId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4001)]
    public async Task InvalidIntakeIsRejectedWithoutChanges(int length)
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceIntakeTests.CreateWork();
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        var service = MaintenanceIntakeTests.CreateService(context, Mock.Of<ISystemClock>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveIntake(
            work.User.Id, work.Vehicle.Organization!.Id, work.Id,
            new SaveMaintenanceIntakeDto { Notes = length == 0 ? "  " : new string('а', length) }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveIntake(
            work.User.Id, work.Vehicle.Organization!.Id, work.Id,
            new SaveMaintenanceIntakeDto { Mileage = -1, Notes = "Проверка отрицательного пробега" }));

        Assert.Null(work.IntakeRecordedAt);
        Assert.False(await context.MaintenanceHistoryEvents.AnyAsync(value => value.MaintenanceId == work.Id));
    }

    [Theory]
    [InlineData("completeness")]
    [InlineData("visibleDamage")]
    [InlineData("notes")]
    public async Task IntakeRejectsEachTextFieldAboveItsLimit(string field)
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceIntakeTests.CreateWork();
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        var service = MaintenanceIntakeTests.CreateService(context, Mock.Of<ISystemClock>());
        var request = new SaveMaintenanceIntakeDto();
        switch (field)
        {
            case "completeness":
                request.Completeness = new string('а', 4001);
                break;
            case "visibleDamage":
                request.VisibleDamage = new string('а', 4001);
                break;
            case "notes":
                request.Notes = new string('а', 4001);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(field));
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveIntake(
            work.User.Id, work.Vehicle.Organization!.Id, work.Id, request));

        Assert.Null(work.IntakeRecordedAt);
        Assert.False(await context.MaintenanceHistoryEvents.AnyAsync(value => value.MaintenanceId == work.Id));
    }

    [Theory]
    [InlineData(9999999, VehicleMileageUnit.Kilometers, true)]
    [InlineData(10000000, VehicleMileageUnit.Kilometers, false)]
    [InlineData(10000000, VehicleMileageUnit.EngineHours, true)]
    public async Task IntakeMileageUsesKilometerLimit(int mileage, VehicleMileageUnit mileageUnit, bool isAccepted)
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceIntakeTests.CreateWork();
        work.Vehicle.MileageUnit = mileageUnit;
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        var service = MaintenanceIntakeTests.CreateService(context, Mock.Of<ISystemClock>());

        var save = () => service.SaveIntake(
            work.User.Id, work.Vehicle.Organization!.Id, work.Id,
            new SaveMaintenanceIntakeDto { Mileage = mileage });
        if (isAccepted)
        {
            await save();
            Assert.Equal(mileage, work.IntakeMileage);

            return;
        }

        var error = await Assert.ThrowsAsync<InvalidOperationException>(save);
        Assert.Equal("Пробег при приёмке не может превышать 9 999 999 км.", error.Message);
        Assert.Null(work.IntakeRecordedAt);
    }

    [Fact]
    public async Task IntakeChecksEditPermissionAndOrganizationOwnership()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceIntakeTests.CreateWork();
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        var access = new Mock<IOrganizationSubscriptionService>(MockBehavior.Strict);
        access.Setup(value => value.EnsureWorkspaceCanWriteAsync("viewer", work.Vehicle.Organization!.Id, OrganizationPermissionCodes.MaintenanceEdit))
            .ThrowsAsync(new InvalidOperationException("Только для чтения"));
        var service = MaintenanceIntakeTests.CreateService(context, Mock.Of<ISystemClock>(), access.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveIntake(
            "viewer", work.Vehicle.Organization!.Id, work.Id, new SaveMaintenanceIntakeDto { Notes = "Без замечаний" }));
        var ownerService = MaintenanceIntakeTests.CreateService(context, Mock.Of<ISystemClock>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => ownerService.SaveIntake(
            work.User.Id, Guid.NewGuid(), work.Id, new SaveMaintenanceIntakeDto { Notes = "Без замечаний" }));
        Assert.Null(work.IntakeNotes);
        access.VerifyAll();
    }

    [Fact]
    public async Task StructuredIntakeFieldsAreIndependentFromMaintenanceMileageAndTrackedInHistory()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceIntakeTests.CreateWork();
        work.Mileage = 75000;
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        var clock = new Mock<ISystemClock>();
        clock.SetupGet(value => value.UtcNow).Returns(new DateTime(2026, 9, 23, 8, 0, 0, DateTimeKind.Utc));
        var service = MaintenanceIntakeTests.CreateService(context, clock.Object);
        var organizationId = work.Vehicle.Organization!.Id;

        await service.SaveIntake(work.User.Id, organizationId, work.Id, new SaveMaintenanceIntakeDto
        {
            Mileage = 74850,
            Completeness = "Два ключа, запасное колесо",
            VisibleDamage = "Царапина на левой двери"
        });
        await service.SaveIntake(work.User.Id, organizationId, work.Id, new SaveMaintenanceIntakeDto
        {
            Mileage = 74850,
            Completeness = "Один ключ, запасное колесо",
            VisibleDamage = "Царапина на левой двери"
        });
        context.ChangeTracker.Clear();

        var saved = await context.Maintenance.SingleAsync(value => value.Id == work.Id);
        Assert.Equal(75000, saved.Mileage);
        Assert.Equal(74850, saved.IntakeMileage);
        Assert.Equal("Один ключ, запасное колесо", saved.IntakeCompleteness);
        Assert.Equal("Царапина на левой двери", saved.IntakeVisibleDamage);
        Assert.Null(saved.IntakeNotes);
        var history = await context.MaintenanceHistoryEvents
            .Where(value => value.MaintenanceId == work.Id)
            .OrderBy(value => value.CreatedAt)
            .ToListAsync();
        Assert.Equal(2, history.Count);
        Assert.Contains("Комплектность: Два ключа", history[1].Description);
        Assert.Contains("Комплектность: Один ключ", history[1].Description);
    }

    [Fact]
    public async Task IntakeActPdfIncludesStructuredFieldsAndBeforePhotos()
    {
        var image = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aNAAAAABJRU5ErkJggg==");
        var model = new MaintenanceIntakePdfModel
        {
            Vehicle = new VehicleInfo { BrandName = "BMW", ModelName = "5 серии", RegistrationPlate = "А123АА154" },
            MaintenanceReference = "12345678-1234-1234-1234-123456789abc",
            RecordedDate = "23.09.2026 08:00 UTC",
            GeneratedDate = "23.09.2026 09:00 UTC",
            Mileage = 74850,
            Completeness = "Два ключа",
            VisibleDamage = "Царапина на двери",
            Notes = "Без иных замечаний",
            BeforePhotos = [new PhotoInfo { ImageData = image, Caption = "До обслуживания" }]
        };

        var pdf = await new QuestPdfGeneratorService().GenerateMaintenanceIntakePdfAsync(model);

        Assert.True(pdf.Length > 1000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public async Task PhotosKeepIdentityAndStageAcrossUploadReadReclassificationAndDelete()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceIntakeTests.CreateWork();
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        var imageService = new Mock<IImageProcessingService>();
        imageService.Setup(value => value.ProcessAndSaveImageAsync(It.IsAny<Stream>(), It.IsAny<ImageProcessingOptions>()))
            .ReturnsAsync("test/intake.jpg");
        imageService.Setup(value => value.GetImageFormatAsync(It.IsAny<Stream>())).ReturnsAsync("image/jpeg");
        var service = MaintenanceIntakeTests.CreatePhotoService(context, imageService.Object);
        var organizationId = work.Vehicle.Organization!.Id;
        await using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        await service.UploadMaintenancePhotosAsync(work.User.Id, organizationId, work.Id,
            new[] { ((Stream)stream, "intake.jpg") }, MaintenancePhotoStage.Before);
        var uploaded = Assert.Single(await service.GetMaintenancePhotosAsync(work.User.Id, organizationId, work.Id));
        Assert.Equal(MaintenancePhotoStage.Before, uploaded.Stage);

        await service.SetMaintenancePhotoStageAsync(work.User.Id, organizationId, work.Id, uploaded.PhotoId, MaintenancePhotoStage.After);
        context.ChangeTracker.Clear();
        var updated = Assert.Single(await service.GetMaintenancePhotosAsync(work.User.Id, organizationId, work.Id));
        Assert.Equal(uploaded.PhotoId, updated.PhotoId);
        Assert.Equal(uploaded.Path, updated.Path);
        Assert.Equal(MaintenancePhotoStage.After, updated.Stage);
        await service.DeleteMaintenancePhotoAsync(work.User.Id, organizationId, work.Id, uploaded.PhotoId);
        Assert.Empty(await service.GetMaintenancePhotosAsync(work.User.Id, organizationId, work.Id));
    }

    [Fact]
    public async Task PhotoStageRejectsForeignMaintenanceUnknownStageAndReadOnlyUser()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceIntakeTests.CreateWork();
        var other = MaintenanceIntakeTests.CreateWork();
        var photo = new MaintenancePhoto
        {
            Maintenance = work, Vehicle = work.Vehicle, User = work.User,
            Path = "test/old.jpg", OriginalFileName = "old.jpg", ContentType = "image/jpeg"
        };
        context.Maintenance.Add(other);
        context.MaintenancePhotos.Add(photo);
        await context.SaveChangesAsync();
        var service = MaintenanceIntakeTests.CreatePhotoService(context, Mock.Of<IImageProcessingService>());
        var organizationId = work.Vehicle.Organization!.Id;
        Assert.Equal(MaintenancePhotoStage.Unspecified, Assert.Single(
            await service.GetMaintenancePhotosAsync(work.User.Id, organizationId, work.Id)).Stage);

        await Assert.ThrowsAsync<ArgumentException>(() => service.SetMaintenancePhotoStageAsync(
            other.User.Id, other.Vehicle.Organization!.Id, other.Id, photo.Id, MaintenancePhotoStage.Before));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SetMaintenancePhotoStageAsync(
            work.User.Id, organizationId, Guid.NewGuid(), photo.Id, MaintenancePhotoStage.Before));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetMaintenancePhotoStageAsync(
            work.User.Id, organizationId, work.Id, photo.Id, (MaintenancePhotoStage)99));
        var viewer = new ApplicationUser();
        context.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = organizationId, User = viewer, RoleCode = OrganizationRoleCodes.Viewer
        });
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetMaintenancePhotoStageAsync(
            viewer.Id, organizationId, work.Id, photo.Id, MaintenancePhotoStage.Before));
        Assert.Equal(MaintenancePhotoStage.Unspecified, photo.Stage);
    }

    [Fact]
    public async Task LinkedIntakePhotoCannotBeMovedToAnotherStageUntilDamageIsRemoved()
    {
        await using var context = fixture.CreateContext();
        var work = MaintenanceIntakeTests.CreateWork();
        var photo = new MaintenancePhoto
        {
            Id = Guid.NewGuid(), Maintenance = work, Vehicle = work.Vehicle, User = work.User,
            Path = "test/intake.jpg", OriginalFileName = "intake.jpg", ContentType = "image/jpeg", Stage = MaintenancePhotoStage.Before
        };
        context.MaintenancePhotos.Add(photo);
        await context.SaveChangesAsync();
        using var scope = MaintenanceIntakeTests.CreateService(context, Mock.Of<ISystemClock>(clock => clock.UtcNow == DateTime.UtcNow));
        var damageService = scope.Get<IMaintenanceIntakeDamageService>();
        var organizationId = work.Vehicle.Organization!.Id;
        var damageId = Guid.NewGuid();
        await damageService.SaveDamage(work.User.Id, organizationId, work.Id, damageId, new SaveMaintenanceIntakeDamageDto
        {
            Version = work.Version, DiagramVersion = MaintenanceIntakeDiagramVersions.SedanV1, ViewCode = "front", DamageType = "chip", X = 0.5, Y = 0.6, PhotoIds = [photo.Id]
        });
        var photoService = MaintenanceIntakeTests.CreatePhotoService(context, Mock.Of<IImageProcessingService>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => photoService.SetMaintenancePhotoStageAsync(
            work.User.Id, organizationId, work.Id, photo.Id, MaintenancePhotoStage.After));
        Assert.Equal(MaintenancePhotoStage.Before, photo.Stage);
        await damageService.DeleteDamage(work.User.Id, organizationId, work.Id, damageId, work.Version);

        await photoService.SetMaintenancePhotoStageAsync(work.User.Id, organizationId, work.Id, photo.Id, MaintenancePhotoStage.After);

        Assert.Equal(MaintenancePhotoStage.After, photo.Stage);
    }

    [Fact]
    public async Task PdfReportRendersIntakeAndBothPhotoStages()
    {
        var image = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aNAAAAABJRU5ErkJggg==");
        var model = new MaintenancePdfModel
        {
            Vehicle = new VehicleInfo { BrandName = "BMW", ModelName = "5 серии", RegistrationPlate = "А123АА154" },
            Maintenance = new MaintenanceInfo
            {
                Date = "13.09.2026", IntakeNotes = "Царапина на двери. Ключи приняты.",
                MaintenanceDetails = [new OpenCRM.Services.Interfaces.Pdf.Dtos.MaintenanceDetail { Name = "Полировка", Price = 8000 }],
                Photos = [new PhotoInfo { ImageData = image, Caption = "До работы" }, new PhotoInfo { ImageData = image, Caption = "После работы" }]
            },
            GeneratedDate = "13.09.2026", CompanyInfo = "opencrm.example.invalid"
        };

        var pdf = await new QuestPdfGeneratorService().GenerateMaintenancePdfAsync(model, includePhotos: true);

        Assert.True(pdf.Length > 1000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public void MigrationModelMatchesCurrentModel()
    {
        using var context = fixture.CreateContext();
        Assert.False(context.Database.HasPendingModelChanges());
    }

    private static MaintenanceTestScope CreateService(OpenCRMContext context, ISystemClock clock, IOrganizationSubscriptionService? access = null)
    {
        if (access == null)
        {
            var subscriptions = new Mock<IOrganizationSubscriptionService>();
            subscriptions.Setup(service => service.HasFeatureAsync(
                    It.IsAny<string>(), It.IsAny<Guid>(), FeatureCodes.MaintenanceIntake))
                .ReturnsAsync(true);
            access = subscriptions.Object;
        }

        return new MaintenanceTestScope(new MaintenanceRepository(context), null!, clock, context,
            access, Mock.Of<IMaintenanceQueryService>(),
            Mock.Of<IMaintenanceSlotQueryService>(), null!);
    }

    private static MaintenancePhotoService CreatePhotoService(OpenCRMContext context, IImageProcessingService imageService)
    {
        var clock = new Mock<ISystemClock>();
        clock.SetupGet(value => value.UtcNow).Returns(DateTime.UtcNow);
        var subscription = new Mock<IOrganizationSubscriptionService>();
        subscription.Setup(service => service.HasFeatureAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), FeatureCodes.VehiclePhotoAlbums))
            .ReturnsAsync(true);
        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddSingleton(subscription.Object);
        services.AddOpenCRMFileStorageInfrastructure();
        using var provider = services.BuildServiceProvider();
        var workspaceAccess = provider.GetRequiredService<IMediaWorkspaceAccess>();

        return new MaintenancePhotoService(
            new MaintenancePhotoRepository(context, NullLogger<MaintenancePhotoRepository>.Instance),
            imageService,
            new MaintenanceRepository(context),
            context,
            subscription.Object,
            new MaintenanceHistoryWriter(clock.Object, context),
            workspaceAccess);
    }

    private static MaintenanceEntity CreateWork()
    {
        var user = new ApplicationUser();
        var vehicle = new Vehicle
        {
            User = user, RegistrationPlate = "А123АА154",
            Organization = new Organization
            {
                Name = "Приёмка детейлинга", OwnerUser = user,
                Members = [new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Owner }]
            }
        };

        return new MaintenanceEntity
        {
            Vehicle = vehicle, User = user, Status = MaintenanceStatus.Planned,
            Type = MaintenanceType.Maintenance, Date = DateTime.UtcNow, DateTimeCreation = DateTime.UtcNow,
            Details = [new() { Name = "Полировка" }]
        };
    }
}
