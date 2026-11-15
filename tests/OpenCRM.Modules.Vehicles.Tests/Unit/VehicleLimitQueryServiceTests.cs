using Moq;
using OpenCRM.Domain.Interfaces.Vehicles;
using OpenCRM.Modules.Vehicles;
using OpenCRM.Services.Interfaces.Features;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Services.Interfaces.Subscriptions.Dtos;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Modules.Vehicles.Tests.Unit;

public sealed class VehicleLimitQueryServiceTests
{
    [Fact]
    public async Task PersonalLimitReportsCurrentSubscriptionPlanAndAvailableCapacity()
    {
        const string userId = "personal-user";
        var vehicles = new Mock<IVehicleRepository>();
        vehicles.Setup(repository => repository.CountVehicles(userId, null, false)).ReturnsAsync(2);
        var subscriptions = new Mock<IPersonalSubscriptionQueryService>();
        subscriptions.Setup(service => service.HasActiveSubscriptionAsync(userId)).ReturnsAsync(true);
        subscriptions.Setup(service => service.IsMaxPlanAsync(userId)).ReturnsAsync(true);
        subscriptions.Setup(service => service.GetUserSubscriptionAsync(userId)).ReturnsAsync(new UserSubscriptionDto
        {
            SubscriptionPlan = new SubscriptionPlanDto { Name = "Премиум" }
        });
        var features = new Mock<IFeatureAccessService>();
        features.Setup(service => service.GetIntValueAsync(userId, Helpers.FeatureCodes.VehiclesLimit, Helpers.PlatformDefaults.VehicleLimit))
            .ReturnsAsync(5);
        var service = new VehicleLimitQueryService(
            vehicles.Object,
            subscriptions.Object,
            Mock.Of<IOrganizationSubscriptionService>(),
            features.Object, Mock.Of<IPlatformCommercialPolicy>(policy => policy.CommercialEnabled == true));

        var result = await service.GetVehicleLimitInfoAsync(userId);

        Assert.Equal(2, result.CurrentVehicleCount);
        Assert.True(result.CanAddMoreVehicles);
        Assert.True(result.HasActiveSubscription);
        Assert.Equal(5, result.MaxVehiclesForUser);
        Assert.True(result.IsMaxPlan);
        Assert.Equal("Премиум", result.CurrentPlanName);
        vehicles.Verify(repository => repository.CountVehicles(userId, null, false), Times.Exactly(2));
    }

    [Fact]
    public async Task ReachedPersonalLimitReportsNoCapacityAndMissingSubscriptionPlan()
    {
        const string userId = "limited-user";
        var vehicles = new Mock<IVehicleRepository>();
        vehicles.Setup(repository => repository.CountVehicles(userId, null, false)).ReturnsAsync(1);
        var subscriptions = new Mock<IPersonalSubscriptionQueryService>();
        subscriptions.Setup(service => service.HasActiveSubscriptionAsync(userId)).ReturnsAsync(false);
        subscriptions.Setup(service => service.IsMaxPlanAsync(userId)).ReturnsAsync(false);
        subscriptions.Setup(service => service.GetUserSubscriptionAsync(userId)).ReturnsAsync((UserSubscriptionDto?)null);
        var features = new Mock<IFeatureAccessService>();
        features.Setup(service => service.GetIntValueAsync(userId, Helpers.FeatureCodes.VehiclesLimit, Helpers.PlatformDefaults.VehicleLimit))
            .ReturnsAsync(1);
        var service = new VehicleLimitQueryService(
            vehicles.Object,
            subscriptions.Object,
            Mock.Of<IOrganizationSubscriptionService>(),
            features.Object, Mock.Of<IPlatformCommercialPolicy>(policy => policy.CommercialEnabled == true));

        var result = await service.GetVehicleLimitInfoAsync(userId, null);

        Assert.Equal(1, result.CurrentVehicleCount);
        Assert.False(result.CanAddMoreVehicles);
        Assert.False(result.HasActiveSubscription);
        Assert.Equal(1, result.MaxVehiclesForUser);
        Assert.False(result.IsMaxPlan);
        Assert.Null(result.CurrentPlanName);

        subscriptions.Setup(subscriptionService => subscriptionService.GetUserSubscriptionAsync(userId))
            .ReturnsAsync(new UserSubscriptionDto { SubscriptionPlan = null! });
        var planWithoutName = await service.GetVehicleLimitInfoAsync(userId);

        Assert.Null(planWithoutName.CurrentPlanName);
    }

    [Fact]
    public async Task OrganizationLimitUsesOrganizationAccessAndPlanWithoutCallingPersonalSubscriptionChecks()
    {
        const string userId = "organization-user";
        var organizationId = Guid.NewGuid();
        var vehicles = new Mock<IVehicleRepository>();
        vehicles.Setup(repository => repository.CountVehicles(userId, organizationId, false)).ReturnsAsync(1);
        var subscriptions = new Mock<IPersonalSubscriptionQueryService>();
        var organizationAccess = new Mock<IOrganizationSubscriptionService>();
        organizationAccess.Setup(service => service.EnsureWorkspaceCanReadAsync(userId, organizationId, OrganizationPermissionCodes.VehiclesView))
            .Returns(Task.CompletedTask);
        organizationAccess.Setup(service => service.GetVehicleLimitAsync(userId, organizationId, 4)).ReturnsAsync(3);
        organizationAccess.Setup(service => service.HasActiveSubscriptionAsync(userId, organizationId)).ReturnsAsync(true);
        organizationAccess.Setup(service => service.GetEffectivePlanAsync(userId, organizationId))
            .ReturnsAsync(new SubscriptionPlanDto { Name = "Команда" });
        var features = new Mock<IFeatureAccessService>();
        features.Setup(service => service.GetIntValueAsync(userId, Helpers.FeatureCodes.VehiclesLimit, Helpers.PlatformDefaults.VehicleLimit))
            .ReturnsAsync(4);
        var service = new VehicleLimitQueryService(vehicles.Object, subscriptions.Object, organizationAccess.Object, features.Object, Mock.Of<IPlatformCommercialPolicy>(policy => policy.CommercialEnabled == true));

        var result = await service.GetVehicleLimitInfoAsync(userId, organizationId);

        Assert.Equal(1, result.CurrentVehicleCount);
        Assert.True(result.CanAddMoreVehicles);
        Assert.True(result.HasActiveSubscription);
        Assert.Equal(3, result.MaxVehiclesForUser);
        Assert.False(result.IsMaxPlan);
        Assert.Equal("Команда", result.CurrentPlanName);
        organizationAccess.Setup(service => service.GetEffectivePlanAsync(userId, organizationId))
            .ReturnsAsync((SubscriptionPlanDto?)null);
        var organizationWithoutPlan = await service.GetVehicleLimitInfoAsync(userId, organizationId);

        Assert.Null(organizationWithoutPlan.CurrentPlanName);
        organizationAccess.Verify(service => service.EnsureWorkspaceCanReadAsync(userId, organizationId, OrganizationPermissionCodes.VehiclesView), Times.Exactly(2));
        subscriptions.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task FreeOrganizationCountsArchivedVehiclesAgainstTheCreationLimit()
    {
        const string userId = "free-organization-user";
        var organizationId = Guid.NewGuid();
        var vehicles = new Mock<IVehicleRepository>();
        vehicles.Setup(repository => repository.CountVehicles(userId, organizationId, null)).ReturnsAsync(1000);
        var organizationAccess = new Mock<IOrganizationSubscriptionService>();
        organizationAccess.Setup(service => service.EnsureWorkspaceCanReadAsync(userId, organizationId, OrganizationPermissionCodes.VehiclesView))
            .Returns(Task.CompletedTask);
        organizationAccess.Setup(service => service.GetWorkspaceAccessAsync(userId, organizationId))
            .ReturnsAsync(new OrganizationWorkspaceAccessDto { IsFreePlan = true });
        organizationAccess.Setup(service => service.GetVehicleLimitAsync(userId, organizationId, 4)).ReturnsAsync(1000);
        organizationAccess.Setup(service => service.HasActiveSubscriptionAsync(userId, organizationId)).ReturnsAsync(false);
        organizationAccess.Setup(service => service.GetEffectivePlanAsync(userId, organizationId))
            .ReturnsAsync(new SubscriptionPlanDto { Name = "Бесплатный" });
        var features = new Mock<IFeatureAccessService>();
        features.Setup(service => service.GetIntValueAsync(userId, Helpers.FeatureCodes.VehiclesLimit, Helpers.PlatformDefaults.VehicleLimit))
            .ReturnsAsync(4);
        var service = new VehicleLimitQueryService(
            vehicles.Object,
            Mock.Of<IPersonalSubscriptionQueryService>(),
            organizationAccess.Object,
            features.Object, Mock.Of<IPlatformCommercialPolicy>(policy => policy.CommercialEnabled == true));

        var result = await service.GetVehicleLimitInfoAsync(userId, organizationId);

        Assert.Equal(1000, result.CurrentVehicleCount);
        Assert.False(result.CanAddMoreVehicles);
        Assert.Equal(1000, result.MaxVehiclesForUser);
        vehicles.Verify(repository => repository.CountVehicles(userId, organizationId, null), Times.Exactly(2));
        vehicles.Verify(repository => repository.CountVehicles(userId, organizationId, false), Times.Never);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NeutralModeIgnoresSavedQuotaAndStillEnforcesWorkspaceReadPermission(bool denyRead)
    {
        var organizationId = Guid.NewGuid();
        var vehicles = new Mock<IVehicleRepository>();
        vehicles.Setup(repository => repository.CountVehicles("user", organizationId, false)).ReturnsAsync(500);
        var access = new Mock<IOrganizationSubscriptionService>();
        access.Setup(service => service.GetVehicleLimitAsync("user", organizationId, It.IsAny<int>())).ReturnsAsync(1);
        if (denyRead)
        {
            access.Setup(service => service.EnsureWorkspaceCanReadAsync("user", organizationId, OrganizationPermissionCodes.VehiclesView))
                .ThrowsAsync(new InvalidOperationException("Доступ запрещён."));
        }
        else
        {
            access.Setup(service => service.EnsureWorkspaceCanReadAsync("user", organizationId, OrganizationPermissionCodes.VehiclesView))
                .Returns(Task.CompletedTask);
        }

        var service = new VehicleLimitQueryService(vehicles.Object, Mock.Of<IPersonalSubscriptionQueryService>(), access.Object,
            Mock.Of<IFeatureAccessService>(), Mock.Of<IPlatformCommercialPolicy>());
        if (denyRead)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetVehicleLimitInfoAsync("user", organizationId));
        }
        else
        {
            var result = await service.GetVehicleLimitInfoAsync("user", organizationId);

            Assert.Equal(500, result.CurrentVehicleCount);
            Assert.True(result.CanAddMoreVehicles);
            Assert.Equal(0, result.MaxVehiclesForUser);
            Assert.False(result.HasActiveSubscription);
            Assert.Null(result.CurrentPlanName);
        }

        access.Verify(service => service.GetVehicleLimitAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<int>()), Times.Never);
        access.Verify(service => service.GetEffectivePlanAsync(It.IsAny<string>(), It.IsAny<Guid>()), Times.Never);
    }
    [Fact]
    public async Task NeutralCommandQuotaChecksMembershipWithoutAddingReadPermissionRequirement()
    {
        var organizationId = Guid.NewGuid();
        var organizations = new Mock<IOrganizationSubscriptionService>();
        organizations.Setup(service => service.GetVehicleLimitAsync("member", organizationId, 0)).ReturnsAsync(1);
        organizations.Setup(service => service.GetVehicleLimitAsync("outsider", organizationId, 0))
            .ThrowsAsync(new InvalidOperationException("Участник не найден."));
        var service = new VehicleLimitQueryService(Mock.Of<IVehicleRepository>(), Mock.Of<IPersonalSubscriptionQueryService>(),
            organizations.Object, Mock.Of<IFeatureAccessService>(), Mock.Of<IPlatformCommercialPolicy>());

        Assert.Equal(0, await service.GetWorkspaceVehicleLimitAsync("member", organizationId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetWorkspaceVehicleLimitAsync("outsider", organizationId));
        organizations.Verify(access => access.EnsureWorkspaceCanReadAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
    }
}
