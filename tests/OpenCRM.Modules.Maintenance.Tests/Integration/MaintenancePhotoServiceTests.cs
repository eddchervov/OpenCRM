using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.Repositories;
using OpenCRM.Infrastructure.FileStorage;
using OpenCRM.Modules.Maintenance;
using OpenCRM.Modules.Maintenance.Photos;
using OpenCRM.Services.Interfaces.Media;
using OpenCRM.Services.Interfaces.Media.Dto;
using OpenCRM.Services.Interfaces.Organizations;
using Helpers;
using Moq;
using Npgsql;

namespace OpenCRM.Modules.Maintenance.Tests.Integration;

public sealed class MaintenancePhotoServiceTests(MaintenanceDatabaseFixture fixture) : IClassFixture<MaintenanceDatabaseFixture>
{
    [Fact]
    public async Task OperationPhotosRequireAssignedEmployeeAndExecutionPermissionButAllowReadForClosedWork()
    {
        await using var context = fixture.CreateContext();
        var data = CreateOrganizationWork(MaintenanceStatus.Planned);
        var otherUser = new ApplicationUser();
        var otherMember = new OrganizationMember { User = otherUser, RoleCode = OrganizationRoleCodes.Performer };
        var otherEmployee = new OrganizationEmployee
        {
            Organization = data.Organization,
            Member = otherMember,
            Name = "Другой исполнитель"
        };
        data.Organization.Members.Add(otherMember);
        data.Work.Details.Single().EmployeeId = data.Employee.Id;
        context.OrganizationEmployees.AddRange(data.Employee, otherEmployee);
        context.Maintenance.Add(data.Work);
        await context.SaveChangesAsync();

        var subscription = new Mock<IOrganizationSubscriptionService>(MockBehavior.Strict);
        subscription.Setup(service => service.EnsureWorkspaceCanWriteAsync(
                data.Performer.Id, data.Organization.Id, OrganizationPermissionCodes.MaintenanceWorkExecute))
            .Returns(Task.CompletedTask);
        subscription.Setup(service => service.EnsureWorkspaceCanReadAsync(
                data.Performer.Id, data.Organization.Id, OrganizationPermissionCodes.MaintenanceWorkView))
            .Returns(Task.CompletedTask);
        subscription.Setup(service => service.EnsureWorkspaceCanReadAsync(
                otherUser.Id, data.Organization.Id, OrganizationPermissionCodes.MaintenanceWorkView))
            .Returns(Task.CompletedTask);
        subscription.Setup(service => service.HasFeatureAsync(
                data.Performer.Id, data.Organization.Id, FeatureCodes.VehiclePhotoAlbums))
            .ReturnsAsync(true);
        var imageService = CreateImageService();
        var service = CreateService(context, imageService.Object, subscription.Object);
        await using var stream = new MemoryStream([1, 2, 3]);

        await service.UploadOperationPhotosAsync(
            data.Performer.Id, data.Organization.Id, data.Work.Id, data.DetailId,
            [((Stream)stream, "operation.jpg")], MaintenancePhotoStage.After);
        var uploaded = Assert.Single(await service.GetOperationPhotosAsync(
            data.Performer.Id, data.Organization.Id, data.Work.Id, data.DetailId));

        Assert.Equal(data.DetailId, uploaded.DetailId);
        Assert.Equal(MaintenancePhotoStage.After, uploaded.Stage);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetOperationPhotosAsync(
            otherUser.Id, data.Organization.Id, data.Work.Id, data.DetailId));

        data.Work.Status = MaintenanceStatus.Completed;
        await context.SaveChangesAsync();
        await using var closedStream = new MemoryStream([4, 5, 6]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UploadOperationPhotosAsync(
            data.Performer.Id, data.Organization.Id, data.Work.Id, data.DetailId,
            [((Stream)closedStream, "closed.jpg")], MaintenancePhotoStage.After));
        Assert.Single(await service.GetOperationPhotosAsync(
            data.Performer.Id, data.Organization.Id, data.Work.Id, data.DetailId));
        subscription.VerifyAll();
    }

    [Fact]
    public async Task OperationPhotoReadIncludesItsBeforeMaintenancePhotosAndKeepsDetailIdentity()
    {
        await using var context = fixture.CreateContext();
        var data = CreateOrganizationWork(MaintenanceStatus.Planned);
        data.Work.Details.Single().EmployeeId = data.Employee.Id;
        var beforePhoto = CreatePhoto(data.Work, data.Work.User.Id, MaintenancePhotoStage.Before, null, "before.jpg");
        beforePhoto.CreatedAt = DateTime.UtcNow.AddMinutes(-1);
        var operationPhoto = CreatePhoto(data.Work, data.Work.User.Id, MaintenancePhotoStage.After, data.DetailId, "operation.jpg");
        operationPhoto.CreatedAt = DateTime.UtcNow;
        var unrelatedPhoto = CreatePhoto(data.Work, data.Work.User.Id, MaintenancePhotoStage.After, null, "after.jpg");
        context.OrganizationEmployees.Add(data.Employee);
        context.Maintenance.Add(data.Work);
        context.MaintenancePhotos.AddRange(beforePhoto, operationPhoto, unrelatedPhoto);
        await context.SaveChangesAsync();

        var subscription = CreateSubscriptionService();
        var service = CreateService(context, Mock.Of<IImageProcessingService>(), subscription.Object);

        var photos = await service.GetOperationPhotosAsync(
            data.Performer.Id, data.Organization.Id, data.Work.Id, data.DetailId);

        Assert.Collection(photos,
            photo =>
            {
                Assert.Equal(beforePhoto.Id, photo.PhotoId);
                Assert.Null(photo.DetailId);
            },
            photo =>
            {
                Assert.Equal(operationPhoto.Id, photo.PhotoId);
                Assert.Equal(data.DetailId, photo.DetailId);
            });
        subscription.Verify(service => service.EnsureWorkspaceCanReadAsync(
            data.Performer.Id, data.Organization.Id, OrganizationPermissionCodes.MaintenanceWorkView), Times.Once);
    }

    [Fact]
    public async Task OperationPhotoReadAndUploadRejectMissingMaintenance()
    {
        await using var context = fixture.CreateContext();
        var data = CreateOrganizationWork(MaintenanceStatus.Planned);
        var service = CreateService(context, Mock.Of<IImageProcessingService>(), CreateSubscriptionService().Object);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetOperationPhotosAsync(
            data.Performer.Id, data.Organization.Id, Guid.NewGuid(), data.DetailId));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.UploadOperationPhotosAsync(
            data.Performer.Id, data.Organization.Id, Guid.NewGuid(), data.DetailId, [], MaintenancePhotoStage.After));
    }

    [Fact]
    public async Task AssignedWorkCanUseMaintenanceEmployeeAsFallbackAssignment()
    {
        await using var context = fixture.CreateContext();
        var data = CreateOrganizationWork(MaintenanceStatus.Planned);
        data.Work.EmployeeId = data.Employee.Id;
        context.OrganizationEmployees.Add(data.Employee);
        context.Maintenance.Add(data.Work);
        await context.SaveChangesAsync();
        var service = CreateService(context, Mock.Of<IImageProcessingService>(), CreateSubscriptionService().Object);

        Assert.Empty(await service.GetOperationPhotosAsync(
            data.Performer.Id, data.Organization.Id, data.Work.Id, data.DetailId));
    }

    [Fact]
    public async Task OperationPhotosRejectUnassignedEmployeeWithoutMaintenanceFallback()
    {
        await using var context = fixture.CreateContext();
        var data = CreateOrganizationWork(MaintenanceStatus.Planned);
        context.OrganizationEmployees.Add(data.Employee);
        context.Maintenance.Add(data.Work);
        await context.SaveChangesAsync();
        var service = CreateService(context, Mock.Of<IImageProcessingService>(), CreateSubscriptionService().Object);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetOperationPhotosAsync(
            data.Performer.Id, data.Organization.Id, data.Work.Id, data.DetailId));
    }

    [Theory]
    [InlineData("catalog")]
    [InlineData("name")]
    [InlineData("quantity")]
    public async Task OperationPhotosRejectMaterialDetails(string materialField)
    {
        await using var context = fixture.CreateContext();
        var data = CreateOrganizationWork(MaintenanceStatus.Planned);
        var detail = data.Work.Details.Single();
        detail.EmployeeId = data.Employee.Id;
        switch (materialField)
        {
            case "catalog":
                detail.MaterialCatalogItemId = Guid.NewGuid();
                break;
            case "name":
                detail.MaterialName = "Полировальный состав";
                break;
            case "quantity":
                detail.MaterialQuantity = 1;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(materialField));
        }

        context.OrganizationEmployees.Add(data.Employee);
        context.Maintenance.Add(data.Work);
        await context.SaveChangesAsync();
        var service = CreateService(context, Mock.Of<IImageProcessingService>(), CreateSubscriptionService().Object);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetOperationPhotosAsync(
            data.Performer.Id, data.Organization.Id, data.Work.Id, data.DetailId));
    }

    [Fact]
    public async Task UploadOperationPhotosRejectsInvalidStageBeforeProcessingFiles()
    {
        await using var context = fixture.CreateContext();
        var data = CreateOrganizationWork(MaintenanceStatus.Planned);
        data.Work.Details.Single().EmployeeId = data.Employee.Id;
        context.OrganizationEmployees.Add(data.Employee);
        context.Maintenance.Add(data.Work);
        await context.SaveChangesAsync();
        var imageService = CreateImageService();
        var service = CreateService(context, imageService.Object, CreateSubscriptionService().Object);
        await using var stream = new MemoryStream([1, 2, 3]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UploadOperationPhotosAsync(
            data.Performer.Id, data.Organization.Id, data.Work.Id, data.DetailId,
            [((Stream)stream, "invalid-stage.jpg")], (MaintenancePhotoStage)999));

        imageService.Verify(image => image.ProcessAndSaveImageAsync(
            It.IsAny<Stream>(), It.IsAny<ImageProcessingOptions>()), Times.Never);
    }

    [Fact]
    public async Task OperationUploadRejectsWhenEmployeeMembershipHasNoActiveEmployeeAtWriteTime()
    {
        var data = CreateOrganizationWork(MaintenanceStatus.Planned);
        data.Work.Details.Single().EmployeeId = data.Employee.Id;
        await using (var seedContext = fixture.CreateContext())
        {
            seedContext.OrganizationEmployees.Add(data.Employee);
            seedContext.Maintenance.Add(data.Work);
            await seedContext.SaveChangesAsync();
        }

        await using var context = fixture.CreateContext();
        var imageService = new Mock<IImageProcessingService>();
        imageService.Setup(image => image.ProcessAndSaveImageAsync(
                It.IsAny<Stream>(), It.IsAny<ImageProcessingOptions>()))
            .Returns(async (Stream _, ImageProcessingOptions _) =>
            {
                await using var concurrentContext = fixture.CreateContext();
                var employee = await concurrentContext.OrganizationEmployees.SingleAsync(item => item.Id == data.Employee.Id);
                concurrentContext.OrganizationEmployees.Remove(employee);
                await concurrentContext.SaveChangesAsync();

                return "test/employee-revoked.jpg";
            });
        imageService.Setup(image => image.GetImageFormatAsync(It.IsAny<Stream>())).ReturnsAsync("image/jpeg");
        var service = CreateService(context, imageService.Object, CreateSubscriptionService().Object);
        await using var stream = new MemoryStream([1, 2, 3]);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.UploadOperationPhotosAsync(
            data.Performer.Id, data.Organization.Id, data.Work.Id, data.DetailId,
            [((Stream)stream, "employee-revoked.jpg")], MaintenancePhotoStage.After));
        Assert.False(await context.MaintenancePhotos.AnyAsync(photo => photo.MaintenanceId == data.Work.Id));
        Assert.False(await context.MaintenanceHistoryEvents.AnyAsync(item => item.MaintenanceId == data.Work.Id));
    }

    [Theory]
    [InlineData(MaintenanceStatus.Completed, false, false)]
    [InlineData(MaintenanceStatus.Cancelled, false, false)]
    [InlineData(MaintenanceStatus.Completed, true, false)]
    [InlineData(MaintenanceStatus.Completed, false, true)]
    public async Task OperationPhotoUploadIsBlockedForClosedOrHandedOverMaintenance(
        MaintenanceStatus status,
        bool readyForHandover,
        bool handedOver)
    {
        await using var context = fixture.CreateContext();
        var data = CreateOrganizationWork(status);
        data.Work.ReadyForHandoverAt = readyForHandover || handedOver ? DateTime.UtcNow.AddMinutes(-1) : null;
        data.Work.HandedOverAt = handedOver ? DateTime.UtcNow : null;
        data.Work.Details.Single().EmployeeId = data.Employee.Id;
        context.OrganizationEmployees.Add(data.Employee);
        context.Maintenance.Add(data.Work);
        await context.SaveChangesAsync();
        var imageService = CreateImageService();
        var service = CreateService(context, imageService.Object, CreateSubscriptionService().Object);
        await using var stream = new MemoryStream([1, 2, 3]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UploadOperationPhotosAsync(
            data.Performer.Id, data.Organization.Id, data.Work.Id, data.DetailId,
            [((Stream)stream, "closed.jpg")], MaintenancePhotoStage.After));

        imageService.Verify(image => image.ProcessAndSaveImageAsync(
            It.IsAny<Stream>(), It.IsAny<ImageProcessingOptions>()), Times.Never);
    }

    [Fact]
    public async Task GetMaintenancePhotoIdsOverloadsReturnPhotosAndRejectMissingMaintenance()
    {
        await using var context = fixture.CreateContext();
        var organizationWork = CreateOrganizationWork(MaintenanceStatus.Planned);
        var organizationPhoto = CreatePhoto(organizationWork.Work, organizationWork.User.Id,
            MaintenancePhotoStage.Before, null, "organization.jpg");
        var personalUser = new ApplicationUser();
        var personalWork = new MaintenanceEntity
        {
            User = personalUser,
            Vehicle = new Vehicle { User = personalUser, RegistrationPlate = "В456ВВ154" },
            Status = MaintenanceStatus.Planned,
            Date = DateTime.UtcNow,
            Details = [new MaintenanceDetail { Name = "Полировка" }]
        };
        var personalPhoto = CreatePhoto(personalWork, personalUser.Id,
            MaintenancePhotoStage.Before, null, "personal.jpg");
        context.Maintenance.AddRange(organizationWork.Work, personalWork);
        context.MaintenancePhotos.AddRange(organizationPhoto, personalPhoto);
        await context.SaveChangesAsync();
        var service = CreateService(context, Mock.Of<IImageProcessingService>());

        Assert.Equal(organizationPhoto.Id, Assert.Single(await service.GetMaintenancePhotoIdsAsync(
            organizationWork.User.Id, organizationWork.Organization.Id, organizationWork.Work.Id)));
        Assert.Equal(personalPhoto.Id, Assert.Single(await service.GetMaintenancePhotoIdsAsync(
            personalUser.Id, personalWork.Id)));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetMaintenancePhotoIdsAsync(
            personalUser.Id, Guid.NewGuid()));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetMaintenancePhotoIdsAsync(
            organizationWork.User.Id, organizationWork.Organization.Id, Guid.NewGuid()));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetMaintenancePhotosAsync(
            organizationWork.User.Id, organizationWork.Organization.Id, Guid.NewGuid()));
    }

    [Fact]
    public async Task OrdinaryPhotoUploadRejectsInvalidStageAndMissingMaintenanceAndRecordsPluralHistory()
    {
        await using var context = fixture.CreateContext();
        var data = CreateOrganizationWork(MaintenanceStatus.Planned);
        context.Maintenance.Add(data.Work);
        await context.SaveChangesAsync();
        var imageService = CreateImageService();
        var service = CreateService(context, imageService.Object);
        await using var first = new MemoryStream([1]);
        await using var second = new MemoryStream([2]);

        await Assert.ThrowsAsync<ArgumentException>(() => service.UploadMaintenancePhotosAsync(
            data.User.Id, data.Organization.Id, Guid.NewGuid(), [], MaintenancePhotoStage.Before));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UploadMaintenancePhotosAsync(
            data.User.Id, data.Organization.Id, data.Work.Id, [], (MaintenancePhotoStage)999));
        await service.UploadMaintenancePhotosAsync(data.User.Id, data.Organization.Id, data.Work.Id,
            [((Stream)first, "first.jpg"), ((Stream)second, "second.jpg")], MaintenancePhotoStage.Before);

        Assert.Equal(2, await context.MaintenancePhotos.CountAsync(photo => photo.MaintenanceId == data.Work.Id));
        var history = await context.MaintenanceHistoryEvents.SingleAsync(item => item.MaintenanceId == data.Work.Id);
        Assert.Equal("Загружены фотографии", history.Title);
    }

    [Fact]
    public async Task OperationUploadRejectsEmptyFilesAndLeavesNoPhotoOrHistoryWhenSecondFileProcessingFails()
    {
        await using var context = fixture.CreateContext();
        var data = CreateOrganizationWork(MaintenanceStatus.Planned);
        data.Work.Details.Single().EmployeeId = data.Employee.Id;
        context.OrganizationEmployees.Add(data.Employee);
        context.Maintenance.Add(data.Work);
        await context.SaveChangesAsync();

        var subscription = CreateSubscriptionService();
        var imageService = new Mock<IImageProcessingService>(MockBehavior.Strict);
        imageService.SetupSequence(service => service.ProcessAndSaveImageAsync(
                It.IsAny<Stream>(), It.IsAny<ImageProcessingOptions>()))
            .ReturnsAsync("test/first.jpg")
            .ThrowsAsync(new InvalidOperationException("Ошибка обработки второго файла"));
        imageService.Setup(service => service.GetImageFormatAsync(It.IsAny<Stream>())).ReturnsAsync("image/jpeg");
        var service = CreateService(context, imageService.Object, subscription.Object);
        await using var first = new MemoryStream([1]);
        await using var second = new MemoryStream([2]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UploadOperationPhotosAsync(
            data.Performer.Id, data.Organization.Id, data.Work.Id, data.DetailId,
            [((Stream)first, "first.jpg"), ((Stream)second, "second.jpg")], MaintenancePhotoStage.After));

        Assert.False(await context.MaintenancePhotos.AnyAsync(photo => photo.MaintenanceId == data.Work.Id));
        Assert.False(await context.MaintenanceHistoryEvents.AnyAsync(item => item.MaintenanceId == data.Work.Id));
        imageService.Verify(service => service.ProcessAndSaveImageAsync(
            It.IsAny<Stream>(), It.IsAny<ImageProcessingOptions>()), Times.Exactly(2));
    }

    [Fact]
    public async Task OperationPhotoAndHistoryAreRolledBackWhenHistorySaveFails()
    {
        var data = CreateOrganizationWork(MaintenanceStatus.Planned);
        await using (var seedContext = fixture.CreateContext())
        {
            data.Work.Details.Single().EmployeeId = data.Employee.Id;
            seedContext.OrganizationEmployees.Add(data.Employee);
            seedContext.Maintenance.Add(data.Work);
            await seedContext.SaveChangesAsync();
        }

        var failedSave = new FailSecondSaveInterceptor();
        await using var context = fixture.CreateContext(failedSave);
        var imageService = CreateImageService();
        var service = CreateService(context, imageService.Object, CreateSubscriptionService().Object);
        await using var stream = new MemoryStream([1, 2, 3]);

        await Assert.ThrowsAsync<DbUpdateException>(() => service.UploadOperationPhotosAsync(
            data.Performer.Id, data.Organization.Id, data.Work.Id, data.DetailId,
            [((Stream)stream, "operation.jpg")], MaintenancePhotoStage.After));
        Assert.Equal(2, failedSave.FailureCount);

        await using var verifyContext = fixture.CreateContext();
        Assert.False(await verifyContext.MaintenancePhotos.AnyAsync(photo => photo.MaintenanceId == data.Work.Id));
        Assert.False(await verifyContext.MaintenanceHistoryEvents.AnyAsync(item => item.MaintenanceId == data.Work.Id));
    }

    [Fact]
    public async Task OperationPhotoUploadRetriesTransientCommitWithoutDuplicatingRowsOrHistory()
    {
        var data = CreateOrganizationWork(MaintenanceStatus.Planned);
        await using (var seedContext = fixture.CreateContext())
        {
            data.Work.Details.Single().EmployeeId = data.Employee.Id;
            seedContext.OrganizationEmployees.Add(data.Employee);
            seedContext.Maintenance.Add(data.Work);
            await seedContext.SaveChangesAsync();
        }

        var failedCommit = new FailFirstCommitInterceptor();
        await using (var context = fixture.CreateContext(failedCommit))
        {
            var service = CreateService(context, CreateImageService().Object, CreateSubscriptionService().Object);
            await using var stream = new MemoryStream([1, 2, 3]);

            await service.UploadOperationPhotosAsync(
                data.Performer.Id, data.Organization.Id, data.Work.Id, data.DetailId,
                [((Stream)stream, "retry.jpg")], MaintenancePhotoStage.After);
        }

        Assert.Equal(1, failedCommit.FailureCount);
        await using var verifyContext = fixture.CreateContext();
        Assert.Single(await verifyContext.MaintenancePhotos.Where(photo => photo.MaintenanceId == data.Work.Id).ToListAsync());
        Assert.Single(await verifyContext.MaintenanceHistoryEvents.Where(item => item.MaintenanceId == data.Work.Id).ToListAsync());
    }

    [Fact]
    public async Task GetMaintenancePhotosRejectsMemberAfterMembershipIsRevoked()
    {
        await using var context = fixture.CreateContext();
        var data = CreateOrganizationWork(MaintenanceStatus.Planned);
        var photo = CreatePhoto(data.Work, data.Work.User.Id, MaintenancePhotoStage.Before, null, "private.jpg");
        var membership = data.Organization.Members.Single(member => member.User == data.Performer);
        context.Maintenance.Add(data.Work);
        context.MaintenancePhotos.Add(photo);
        await context.SaveChangesAsync();

        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddSingleton(CreateSubscriptionService().Object);
        services.AddOpenCRMFileStorageInfrastructure();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var service = CreateService(context, Mock.Of<IImageProcessingService>(),
            scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionService>(),
            scope.ServiceProvider.GetRequiredService<IMediaWorkspaceAccess>());
        Assert.Single(await service.GetMaintenancePhotosAsync(
            data.Performer.Id, data.Organization.Id, data.Work.Id));

        context.OrganizationMembers.Remove(membership);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetMaintenancePhotosAsync(
            data.Performer.Id, data.Organization.Id, data.Work.Id));
    }

    [Fact]
    public async Task PersonalPhotoOverloadsUploadAndDeleteWithoutOrganizationId()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var work = new MaintenanceEntity
        {
            User = user,
            Vehicle = new Vehicle { User = user, RegistrationPlate = "А123АА154" },
            Status = MaintenanceStatus.Planned,
            Date = DateTime.UtcNow,
            Details = [new MaintenanceDetail { Name = "Полировка" }]
        };
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        var imageService = CreateImageService();
        var service = CreateService(context, imageService.Object);
        await using var stream = new MemoryStream([1, 2, 3]);

        await service.UploadMaintenancePhotosAsync(user.Id, work.Id, [((Stream)stream, "personal.jpg")]);
        var photo = Assert.Single(await service.GetMaintenancePhotosAsync(user.Id, null, work.Id));
        await service.DeleteMaintenancePhotoAsync(user.Id, work.Id, photo.PhotoId);

        Assert.Empty(await service.GetMaintenancePhotosAsync(user.Id, null, work.Id));
    }

    [Fact]
    public async Task EmptyOrdinaryUploadIsANoopWhileEmptyOperationUploadIsRejected()
    {
        await using var context = fixture.CreateContext();
        var data = CreateOrganizationWork(MaintenanceStatus.Planned);
        data.Work.Details.Single().EmployeeId = data.Employee.Id;
        context.OrganizationEmployees.Add(data.Employee);
        context.Maintenance.Add(data.Work);
        await context.SaveChangesAsync();
        var service = CreateService(context, Mock.Of<IImageProcessingService>(), CreateSubscriptionService().Object);

        await service.UploadMaintenancePhotosAsync(data.Work.User.Id, data.Organization.Id, data.Work.Id, []);

        Assert.False(await context.MaintenancePhotos.AnyAsync(photo => photo.MaintenanceId == data.Work.Id));
        Assert.False(await context.MaintenanceHistoryEvents.AnyAsync(item => item.MaintenanceId == data.Work.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UploadOperationPhotosAsync(
            data.Performer.Id, data.Organization.Id, data.Work.Id, data.DetailId, [], MaintenancePhotoStage.After));
    }

    [Fact]
    public async Task PhotoStageRejectsMissingMaintenancePhotoAndInvalidStageButUnchangedStageAddsNoHistory()
    {
        await using var context = fixture.CreateContext();
        var data = CreateOrganizationWork(MaintenanceStatus.Planned);
        var photo = CreatePhoto(data.Work, data.Work.User.Id, MaintenancePhotoStage.Before, null, "stage.jpg");
        var otherWork = new MaintenanceEntity
        {
            User = data.Work.User,
            Vehicle = data.Work.Vehicle,
            Status = MaintenanceStatus.Planned,
            Date = DateTime.UtcNow,
            Details = [new MaintenanceDetail { Name = "Другая работа" }]
        };
        var foreignPhoto = CreatePhoto(otherWork, data.Work.User.Id, MaintenancePhotoStage.Before, null, "foreign.jpg");
        context.Maintenance.Add(data.Work);
        context.Maintenance.Add(otherWork);
        context.MaintenancePhotos.AddRange(photo, foreignPhoto);
        await context.SaveChangesAsync();
        var service = CreateService(context, Mock.Of<IImageProcessingService>());

        await Assert.ThrowsAsync<ArgumentException>(() => service.SetMaintenancePhotoStageAsync(
            data.Work.User.Id, data.Organization.Id, Guid.NewGuid(), photo.Id, MaintenancePhotoStage.After));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SetMaintenancePhotoStageAsync(
            data.Work.User.Id, data.Organization.Id, data.Work.Id, Guid.NewGuid(), MaintenancePhotoStage.After));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SetMaintenancePhotoStageAsync(
            data.Work.User.Id, data.Organization.Id, data.Work.Id, foreignPhoto.Id, MaintenancePhotoStage.After));
        await Assert.ThrowsAsync<ArgumentException>(() => service.DeleteMaintenancePhotoAsync(
            data.Work.User.Id, data.Organization.Id, data.Work.Id, Guid.NewGuid()));
        await Assert.ThrowsAsync<ArgumentException>(() => service.DeleteMaintenancePhotoAsync(
            data.Work.User.Id, data.Organization.Id, data.Work.Id, foreignPhoto.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetMaintenancePhotoStageAsync(
            data.Work.User.Id, data.Organization.Id, data.Work.Id, photo.Id, (MaintenancePhotoStage)999));
        await service.SetMaintenancePhotoStageAsync(
            data.Work.User.Id, data.Organization.Id, data.Work.Id, photo.Id, MaintenancePhotoStage.Before);

        Assert.False(await context.MaintenanceHistoryEvents.AnyAsync(item => item.MaintenanceId == data.Work.Id));
    }

    [Fact]
    public async Task ChangingPhotoStageToBeforeAndUnspecifiedRecordsBothHistoryLabels()
    {
        await using var context = fixture.CreateContext();
        var data = CreateOrganizationWork(MaintenanceStatus.Planned);
        var photo = CreatePhoto(data.Work, data.Work.User.Id, MaintenancePhotoStage.After, null, "stage-labels.jpg");
        context.Maintenance.Add(data.Work);
        context.MaintenancePhotos.Add(photo);
        await context.SaveChangesAsync();
        var service = CreateService(context, Mock.Of<IImageProcessingService>());

        await service.SetMaintenancePhotoStageAsync(
            data.Work.User.Id, data.Organization.Id, data.Work.Id, photo.Id, MaintenancePhotoStage.Before);
        await service.SetMaintenancePhotoStageAsync(
            data.Work.User.Id, data.Organization.Id, data.Work.Id, photo.Id, MaintenancePhotoStage.Unspecified);

        var historyDescriptions = await context.MaintenanceHistoryEvents
            .Where(item => item.MaintenanceId == data.Work.Id)
            .Select(item => item.Description)
            .ToListAsync();
        Assert.Contains("stage-labels.jpg: До", historyDescriptions);
        Assert.Contains("stage-labels.jpg: Без категории", historyDescriptions);
        Assert.Equal(MaintenancePhotoStage.Unspecified,
            (await context.MaintenancePhotos.SingleAsync(item => item.Id == photo.Id)).Stage);
    }

    [Fact]
    public async Task OperationUploadRecordsActorAndTruncatesHistoryDescription()
    {
        await using var context = fixture.CreateContext();
        var data = CreateOrganizationWork(MaintenanceStatus.Planned);
        data.Performer.Email = "performer@example.test";
        data.Work.Details.Single().EmployeeId = data.Employee.Id;
        context.OrganizationEmployees.Add(data.Employee);
        context.Maintenance.Add(data.Work);
        await context.SaveChangesAsync();
        var service = CreateService(context, CreateImageService().Object, CreateSubscriptionService().Object);
        var streams = Enumerable.Range(1, 5)
            .Select(index => new MemoryStream([1, (byte)index]))
            .ToArray();
        var files = Enumerable.Range(1, 5)
            .Select(index => ((Stream)streams[index - 1], $"{new string((char)('a' + index), 220)}.jpg"))
            .ToArray();

        using var firstStream = streams[0];
        using var secondStream = streams[1];
        using var thirdStream = streams[2];
        using var fourthStream = streams[3];
        using var fifthStream = streams[4];

        await service.UploadOperationPhotosAsync(
            data.Performer.Id, data.Organization.Id, data.Work.Id, data.DetailId, files, MaintenancePhotoStage.After);

        var history = await context.MaintenanceHistoryEvents.SingleAsync(item => item.MaintenanceId == data.Work.Id);
        Assert.Equal("performer@example.test", history.ActorDisplayName);
        Assert.True(history.Description!.Length <= 1000);
        Assert.Contains("Файлы", history.Description);
    }

    private static MaintenancePhotoService CreateService(
        OpenCRMContext context,
        IImageProcessingService imageService,
        IOrganizationSubscriptionService? subscription = null,
        IMediaWorkspaceAccess? workspaceAccess = null)
    {
        var clock = new Mock<ISystemClock>();
        clock.SetupGet(value => value.UtcNow).Returns(new DateTime(2026, 10, 12, 10, 0, 0, DateTimeKind.Utc));
        var access = workspaceAccess ?? Mock.Of<IMediaWorkspaceAccess>(value =>
            value.EnsureCanReadWorkspaceAsync(It.IsAny<string>(), It.IsAny<Guid?>()) == Task.CompletedTask
            && value.EnsureCanWriteWorkspaceAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string>()) == Task.CompletedTask);

        return new MaintenancePhotoService(
            new MaintenancePhotoRepository(context, NullLogger<MaintenancePhotoRepository>.Instance),
            imageService,
            new MaintenanceRepository(context),
            context,
            subscription ?? CreateSubscriptionService().Object,
            new MaintenanceHistoryWriter(clock.Object, context),
            access);
    }

    private static Mock<IOrganizationSubscriptionService> CreateSubscriptionService()
    {
        var subscription = new Mock<IOrganizationSubscriptionService>();
        subscription.Setup(service => service.EnsureWorkspaceCanReadAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        subscription.Setup(service => service.EnsureWorkspaceCanWriteAsync(
                It.IsAny<string>(), It.IsAny<Guid>()))
            .Returns(Task.CompletedTask);
        subscription.Setup(service => service.EnsureWorkspaceCanWriteAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        subscription.Setup(service => service.HasFeatureAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), FeatureCodes.VehiclePhotoAlbums))
            .ReturnsAsync(true);

        return subscription;
    }

    private static Mock<IImageProcessingService> CreateImageService()
    {
        var imageService = new Mock<IImageProcessingService>();
        imageService.Setup(service => service.ProcessAndSaveImageAsync(
                It.IsAny<Stream>(), It.IsAny<ImageProcessingOptions>()))
            .ReturnsAsync($"test/{Guid.NewGuid():N}.jpg");
        imageService.Setup(service => service.GetImageFormatAsync(It.IsAny<Stream>())).ReturnsAsync("image/jpeg");

        return imageService;
    }

    private static (ApplicationUser User, ApplicationUser Performer, Organization Organization,
        OrganizationEmployee Employee, MaintenanceEntity Work, Guid DetailId) CreateOrganizationWork(MaintenanceStatus status)
    {
        var user = new ApplicationUser();
        var performer = new ApplicationUser();
        var ownerMember = new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Owner };
        var performerMember = new OrganizationMember { User = performer, RoleCode = OrganizationRoleCodes.Performer };
        var organization = new Organization
        {
            Name = "Фото обслуживания",
            OwnerUser = user,
            Members = [ownerMember, performerMember]
        };
        var employee = new OrganizationEmployee { Organization = organization, Member = performerMember, Name = "Исполнитель" };
        var detailId = Guid.NewGuid();
        var work = new MaintenanceEntity
        {
            User = user,
            Vehicle = new Vehicle { User = user, Organization = organization, RegistrationPlate = "А123АА154" },
            Status = status,
            Date = DateTime.UtcNow,
            Details = [new MaintenanceDetail { DetailId = detailId, Name = "Полировка" }]
        };

        return (user, performer, organization, employee, work, detailId);
    }

    private static MaintenancePhoto CreatePhoto(
        MaintenanceEntity work,
        string userId,
        MaintenancePhotoStage stage,
        Guid? detailId,
        string fileName) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Maintenance = work,
        Vehicle = work.Vehicle,
        Path = $"test/{fileName}",
        Stage = stage,
        Type = MediaType.MaintenancePhoto,
        ContentType = "image/jpeg",
        OriginalFileName = fileName,
        DetailId = detailId
    };

    private sealed class FailSecondSaveInterceptor : SaveChangesInterceptor
    {
        public int FailureCount { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            this.FailureCount++;
            if (this.FailureCount == 2)
            {
                return ValueTask.FromException<InterceptionResult<int>>(
                    new DbUpdateException("Не удалось записать историю фото.", new InvalidOperationException()));
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class FailFirstCommitInterceptor : DbTransactionInterceptor
    {
        public int FailureCount { get; private set; }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (this.FailureCount > 0)
            {
                return ValueTask.FromResult(result);
            }

            this.FailureCount++;

            return ValueTask.FromException<InterceptionResult>(
                new PostgresException("Имитирован временный конфликт при загрузке фото.", "ERROR", "ERROR", "40001"));
        }
    }
}
