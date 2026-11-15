using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Helpers;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.Repositories;
using OpenCRM.Modules.Billing.Features;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Modules.Maintenance.Contracts.Photos;
using OpenCRM.Modules.Maintenance.Photos;
using OpenCRM.Modules.Organizations;
using OpenCRM.Services.Interfaces.Media;
using OpenCRM.Services.Interfaces.Media.Dto;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Testing;

namespace OpenCRM.Modules.Maintenance.Tests.Integration;

public sealed class MaintenanceProAccessTests(MaintenanceDatabaseFixture fixture) : IClassFixture<MaintenanceDatabaseFixture>
{
    [Fact]
    public async Task FreeOwnerCannotSaveIntakeOrDamageAndNoDataIsWritten()
    {
        await using var context = fixture.CreateContext();
        await FeatureBootstrapper.EnsureDefaultsAsync(context);
        // Проверка опирается на тарифы организации: коммерческий режим включается явно.
        await FeatureBootstrapper.EnsureCommercialPlansAsync(context);
        var work = MaintenanceProAccessTests.CreateWork();
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        using var scope = MaintenanceProAccessTests.CreateScope(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.SaveIntake(
            work.User.Id,
            work.Vehicle.Organization!.Id,
            work.Id,
            new SaveMaintenanceIntakeDto { Notes = "Тестовая приёмка" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Get<IMaintenanceIntakeDamageService>().SaveDamage(
            work.User.Id,
            work.Vehicle.Organization!.Id,
            work.Id,
            Guid.NewGuid(),
            MaintenanceProAccessTests.CreateDamage(work.Version)));

        context.ChangeTracker.Clear();
        var persisted = await context.Maintenance.SingleAsync(item => item.Id == work.Id);

        Assert.Null(persisted.IntakeNotes);
        Assert.Empty(await context.MaintenanceIntakeDamages.Where(item => item.MaintenanceId == work.Id).ToListAsync());
    }

    [Fact]
    public async Task ProOwnerCanSaveIntakeAndDamage()
    {
        await using var context = fixture.CreateContext();
        await FeatureBootstrapper.EnsureDefaultsAsync(context);
        // Проверка опирается на тарифы организации: коммерческий режим включается явно.
        await FeatureBootstrapper.EnsureCommercialPlansAsync(context);
        var work = MaintenanceProAccessTests.CreateWork();
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        await MaintenanceProAccessTests.EnableProAsync(context, work.Vehicle.Organization!.Id);
        using var scope = MaintenanceProAccessTests.CreateScope(context);

        await scope.SaveIntake(
            work.User.Id,
            work.Vehicle.Organization.Id,
            work.Id,
            new SaveMaintenanceIntakeDto { Notes = "Тестовая приёмка PRO" });
        context.ChangeTracker.Clear();
        var persisted = await context.Maintenance.SingleAsync(item => item.Id == work.Id);
        await scope.Get<IMaintenanceIntakeDamageService>().SaveDamage(
            work.User.Id,
            work.Vehicle.Organization.Id,
            work.Id,
            Guid.NewGuid(),
            MaintenanceProAccessTests.CreateDamage(persisted.Version));

        context.ChangeTracker.Clear();
        persisted = await context.Maintenance.SingleAsync(item => item.Id == work.Id);
        Assert.Equal("Тестовая приёмка PRO", persisted.IntakeNotes);
        Assert.Single(await context.MaintenanceIntakeDamages.Where(item => item.MaintenanceId == work.Id).ToListAsync());
    }

    [Theory]
    [InlineData(OrganizationRoleCodes.Viewer)]
    [InlineData("outsider")]
    public async Task ProViewerAndNonMemberCannotSaveIntake(string roleCode)
    {
        await using var context = fixture.CreateContext();
        await FeatureBootstrapper.EnsureDefaultsAsync(context);
        // Проверка опирается на тарифы организации: коммерческий режим включается явно.
        await FeatureBootstrapper.EnsureCommercialPlansAsync(context);
        var work = MaintenanceProAccessTests.CreateWork();
        var organization = work.Vehicle.Organization!;
        var actor = new ApplicationUser();
        context.ApplicationUsers.Add(actor);
        if (roleCode != "outsider")
        {
            organization.Members.Add(new OrganizationMember { User = actor, RoleCode = roleCode });
        }

        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        await MaintenanceProAccessTests.EnableProAsync(context, organization.Id);
        using var scope = MaintenanceProAccessTests.CreateScope(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.SaveIntake(
            actor.Id,
            organization.Id,
            work.Id,
            new SaveMaintenanceIntakeDto { Notes = "Запрещённая запись" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Get<IMaintenanceIntakeDamageService>().SaveDamage(
            actor.Id,
            organization.Id,
            work.Id,
            Guid.NewGuid(),
            MaintenanceProAccessTests.CreateDamage(work.Version)));
        context.ChangeTracker.Clear();

        Assert.Null(await context.Maintenance.Where(item => item.Id == work.Id)
            .Select(item => item.IntakeNotes).SingleAsync());
        Assert.Empty(await context.MaintenanceIntakeDamages.Where(item => item.MaintenanceId == work.Id).ToListAsync());
    }

    [Fact]
    public async Task FreeOwnerCannotUploadOrderPhotoAndCanReadSavedHistory()
    {
        await using var context = fixture.CreateContext();
        await FeatureBootstrapper.EnsureDefaultsAsync(context);
        // Проверка опирается на тарифы организации: коммерческий режим включается явно.
        await FeatureBootstrapper.EnsureCommercialPlansAsync(context);
        var work = MaintenanceProAccessTests.CreateWork();
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        var photo = new MaintenancePhoto
        {
            Id = Guid.NewGuid(),
            UserId = work.User.Id,
            Maintenance = work,
            Vehicle = work.Vehicle,
            Path = "test/existing-before.jpg",
            Stage = MaintenancePhotoStage.Before,
            Type = MediaType.MaintenancePhoto,
            ContentType = "image/jpeg",
            OriginalFileName = "existing-before.jpg"
        };
        context.MaintenancePhotos.Add(photo);
        await context.SaveChangesAsync();
        var (subscriptions, workspaceAccess) = MaintenanceProAccessTests.CreateSubscriptionService(context);
        var imageService = new Mock<IImageProcessingService>(MockBehavior.Strict);
        var clock = MaintenanceProAccessTests.CreateClock();
        var service = new MaintenancePhotoService(
            new MaintenancePhotoRepository(context, NullLogger<MaintenancePhotoRepository>.Instance),
            imageService.Object,
            new MaintenanceRepository(context),
            context,
            subscriptions.Object,
            new MaintenanceHistoryWriter(clock, context),
            new MaintenanceProAccessTests.MediaWorkspaceAccessFacade(workspaceAccess));
        await using var uploadStream = new MemoryStream([1, 2, 3]);
        var beforeCount = await context.MaintenancePhotos.CountAsync(item => item.MaintenanceId == work.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UploadMaintenancePhotosAsync(
            work.User.Id,
            work.Vehicle.Organization!.Id,
            work.Id,
            [((Stream)uploadStream, "new-after.jpg")],
            MaintenancePhotoStage.After));
        var history = await service.GetMaintenancePhotosAsync(work.User.Id, work.Vehicle.Organization!.Id, work.Id);

        Assert.Equal(beforeCount, await context.MaintenancePhotos.CountAsync(item => item.MaintenanceId == work.Id));
        Assert.Equal(photo.Id, Assert.Single(history).PhotoId);
        imageService.VerifyNoOtherCalls();
    }

    private static MaintenanceTestScope CreateScope(OpenCRMContext context)
    {
        var (subscriptions, _) = MaintenanceProAccessTests.CreateSubscriptionService(context);

        return new MaintenanceTestScope(
            new MaintenanceRepository(context),
            null!,
            MaintenanceProAccessTests.CreateClock(),
            context,
            subscriptions.Object,
            Mock.Of<IMaintenanceQueryService>(),
            Mock.Of<IMaintenanceSlotQueryService>(),
            null!);
    }

    private static (Mock<IOrganizationSubscriptionService> Subscriptions, IWorkspaceAccessService WorkspaceAccess)
        CreateSubscriptionService(OpenCRMContext context)
    {
        var permissions = new OrganizationPermissionService(context, Mock.Of<IOrganizationAuditService>());
        var workspaceAccess = OrganizationBillingTestServices.CreateAccess(context, permissions);
        var entitlements = OrganizationBillingTestServices.CreateEntitlement(context);
        var subscriptions = new Mock<IOrganizationSubscriptionService>();
        subscriptions.Setup(service => service.EnsureWorkspaceCanReadAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns((string userId, Guid organizationId, string permissionCode) => workspaceAccess
                .EnsureWorkspaceCanReadAsync(userId, organizationId, permissionCode));
        subscriptions.Setup(service => service.EnsureWorkspaceCanWriteAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns((string userId, Guid organizationId, string permissionCode) => workspaceAccess
                .EnsureWorkspaceCanWriteAsync(userId, organizationId, permissionCode));
        subscriptions.Setup(service => service.EnsureWorkspaceCanWriteAsync(
                It.IsAny<string>(), It.IsAny<Guid>()))
            .Returns((string userId, Guid organizationId) => workspaceAccess.EnsureWorkspaceCanWriteAsync(userId, organizationId));
        subscriptions.Setup(service => service.HasFeatureAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns((string userId, Guid organizationId, string featureCode) => entitlements
                .HasFeatureAsync(userId, organizationId, featureCode));

        return (subscriptions, workspaceAccess);
    }

    private static async Task EnableProAsync(OpenCRMContext context, Guid organizationId)
    {
        var plan = await context.SubscriptionPlans.SingleAsync(item => item.IsDefaultPlan
            && item.PlanTypeCode == SubscriptionPlanCatalogCodes.PlanTypes.Business);
        var intakeFeature = await context.SubscriptionPlanFeatures.SingleAsync(item =>
            item.SubscriptionPlanId == plan.Id && item.FeatureDefinition.Code == FeatureCodes.MaintenanceIntake);
        Assert.True(intakeFeature.BoolValue);
        context.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            OrganizationId = organizationId,
            SubscriptionPlan = plan,
            StartDate = DateTime.UtcNow.AddMinutes(-1),
            EndDate = DateTime.UtcNow.AddDays(1)
        });
        await context.SaveChangesAsync();
    }

    private static ISystemClock CreateClock()
    {
        var clock = new Mock<ISystemClock>();
        clock.SetupGet(value => value.UtcNow).Returns(new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc));

        return clock.Object;
    }

    private static SaveMaintenanceIntakeDamageDto CreateDamage(uint version) => new()
    {
        Version = version,
        DiagramVersion = MaintenanceIntakeDiagramVersions.SedanV1,
        ViewCode = "left",
        DamageType = "scratch",
        X = 0.5,
        Y = 0.5,
        Description = "Тестовая царапина"
    };

    private static MaintenanceEntity CreateWork()
    {
        var user = new ApplicationUser();
        var organization = new Organization
        {
            Name = "Приёмка детейлинга PRO",
            PrimaryScenarioCode = "detailing",
            OwnerUser = user,
            Members = [new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Owner }]
        };
        var vehicle = new Vehicle
        {
            User = user,
            RegistrationPlate = "А123АА154",
            Organization = organization
        };

        return new MaintenanceEntity
        {
            Vehicle = vehicle,
            User = user,
            Status = MaintenanceStatus.Planned,
            Type = MaintenanceType.Maintenance,
            Date = DateTime.UtcNow,
            DateTimeCreation = DateTime.UtcNow,
            Details = [new() { Name = "Полировка" }]
        };
    }

    private sealed class MediaWorkspaceAccessFacade(IWorkspaceAccessService workspaceAccess) : IMediaWorkspaceAccess
    {
        public Task EnsureCanReadWorkspaceAsync(string userId, Guid? organizationId)
        {
            if (!organizationId.HasValue)
            {
                return Task.CompletedTask;
            }

            return workspaceAccess.EnsureWorkspaceCanReadAsync(
                userId,
                organizationId.Value,
                OrganizationPermissionCodes.MaintenanceView);
        }

        public Task EnsureCanWriteWorkspaceAsync(string userId, Guid? organizationId, string? permissionCode = null)
        {
            if (!organizationId.HasValue)
            {
                return Task.CompletedTask;
            }

            return workspaceAccess.EnsureWorkspaceCanWriteAsync(
                userId,
                organizationId.Value,
                permissionCode ?? OrganizationPermissionCodes.MaintenanceEdit);
        }
    }
}
