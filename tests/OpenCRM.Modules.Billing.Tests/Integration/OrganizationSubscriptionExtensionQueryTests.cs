using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Billing.Tests.Integration;

[Collection(BillingDatabaseCollection.Name)]
public sealed class OrganizationSubscriptionExtensionQueryTests(BillingDatabaseFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ExtensionQueriesReturnNoActiveSubscriptionForNoneAndExpiredRows()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        using var provider = this.CreateService(context);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionExtensionQueryService>();

        Assert.False(await service.CanExtendSubscriptionAsync(organization.OwnerUserId, organization.Id));
        Assert.Equal(
            (false, 0, "У рабочего пространства нет активного тарифа для продления."),
            await service.GetSubscriptionExtensionInfoAsync(organization.OwnerUserId, organization.Id));

        var plan = await fixture.CreatePlanAsync(context, "Истёкший тариф");
        await fixture.AddSubscriptionAsync(context, organization, plan, DateTime.UtcNow.AddDays(-1));

        Assert.False(await service.CanExtendSubscriptionAsync(organization.OwnerUserId, organization.Id));
        Assert.Equal(
            (false, 0, "У рабочего пространства нет активного тарифа для продления."),
            await service.GetSubscriptionExtensionInfoAsync(organization.OwnerUserId, organization.Id));
    }

    [Fact]
    public async Task ExtensionQueriesDoNotAllowDemoOrDefaultPlans()
    {
        await using var context = fixture.CreateContext();
        var demoOrganization = await fixture.CreateOrganizationAsync(context);
        var defaultOrganization = await fixture.CreateOrganizationAsync(context);
        var demoPlan = await fixture.CreatePlanAsync(context, "Демо", isDemo: true);
        var defaultPlan = await fixture.CreatePlanAsync(context, "Базовый", isDefault: true);
        await fixture.AddSubscriptionAsync(context, demoOrganization, demoPlan, DateTime.UtcNow.AddDays(2.1));
        await fixture.AddSubscriptionAsync(context, defaultOrganization, defaultPlan, DateTime.UtcNow.AddDays(4));
        using var provider = this.CreateService(context);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionExtensionQueryService>();

        Assert.False(await service.CanExtendSubscriptionAsync(demoOrganization.OwnerUserId, demoOrganization.Id));
        var demoInfo = await service.GetSubscriptionExtensionInfoAsync(demoOrganization.OwnerUserId, demoOrganization.Id);
        Assert.False(demoInfo.CanExtend);
        Assert.Equal(3, demoInfo.DaysUntilExpiry);
        Assert.Equal("Демо-тариф нельзя продлить.", demoInfo.Message);

        Assert.False(await service.CanExtendSubscriptionAsync(defaultOrganization.OwnerUserId, defaultOrganization.Id));
        Assert.Equal(
            (false, 0, "Базовый тариф не продлевается. Для этого рабочего пространства можно подключить платный тариф."),
            await service.GetSubscriptionExtensionInfoAsync(defaultOrganization.OwnerUserId, defaultOrganization.Id));
    }

    [Fact]
    public async Task PaidSubscriptionCanBeExtendedWithinSevenDaysAndInfoRoundsDaysUp()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var plan = await fixture.CreatePlanAsync(context, "Платный тариф");
        await fixture.AddSubscriptionAsync(context, organization, plan, DateTime.UtcNow.AddDays(6));
        using var provider = this.CreateService(context);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionExtensionQueryService>();

        Assert.True(await service.CanExtendSubscriptionAsync(organization.OwnerUserId, organization.Id));
        var info = await service.GetSubscriptionExtensionInfoAsync(organization.OwnerUserId, organization.Id);

        Assert.True(info.CanExtend);
        Assert.Equal(6, info.DaysUntilExpiry);
        Assert.Equal("Тариф можно продлить. Осталось дней: 6.", info.Message);
    }

    [Fact]
    public async Task ExtensionQueriesUseSubscriptionWithLatestEndDate()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var earlierPlan = await fixture.CreatePlanAsync(context, "Близкий срок");
        var laterPlan = await fixture.CreatePlanAsync(context, "Дальний срок");
        await fixture.AddSubscriptionAsync(context, organization, earlierPlan, DateTime.UtcNow.AddDays(3));
        await fixture.AddSubscriptionAsync(context, organization, laterPlan, DateTime.UtcNow.AddDays(12));
        using var provider = this.CreateService(context);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionExtensionQueryService>();

        Assert.False(await service.CanExtendSubscriptionAsync(organization.OwnerUserId, organization.Id));
        var info = await service.GetSubscriptionExtensionInfoAsync(organization.OwnerUserId, organization.Id);

        Assert.False(info.CanExtend);
        Assert.InRange(info.DaysUntilExpiry, 11, 12);
        Assert.StartsWith("Продление откроется за 7 дней до окончания. Осталось дней: ", info.Message);
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("archived")]
    [InlineData("deleted")]
    public async Task ExtensionQueriesRejectInactiveArchivedAndDeletedOrganizations(string state)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        switch (state)
        {
            case "inactive":
                organization.IsActive = false;
                break;
            case "archived":
                organization.IsArchived = true;
                break;
            case "deleted":
                organization.IsDeleted = true;
                break;
        }

        await context.SaveChangesAsync();
        var permissions = new Mock<IOrganizationPermissionService>(MockBehavior.Strict);
        using var provider = this.CreateService(context, permissions.Object);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionExtensionQueryService>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CanExtendSubscriptionAsync(organization.OwnerUserId, organization.Id));

        Assert.Equal("Организация не найдена.", exception.Message);
        permissions.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExtensionQueriesKeepSeparateMessageForNonMembers()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var permissions = new Mock<IOrganizationPermissionService>(MockBehavior.Strict);
        using var provider = this.CreateService(context, permissions.Object);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionExtensionQueryService>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetSubscriptionExtensionInfoAsync("not-a-member", organization.Id));

        Assert.Equal("Организация не найдена или доступ закрыт.", exception.Message);
        permissions.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExtensionQueriesRequireBillingManagePermission()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var permissions = new Mock<IOrganizationPermissionService>(MockBehavior.Strict);
        permissions.Setup(service => service.HasPermissionAsync(
                organization.OwnerUserId,
                organization.Id,
                OrganizationPermissionCodes.BillingManage))
            .ReturnsAsync(false);
        using var provider = this.CreateService(context, permissions.Object);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionExtensionQueryService>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CanExtendSubscriptionAsync(organization.OwnerUserId, organization.Id));

        Assert.Equal(
            OrganizationPermissionCatalog.BuildDeniedMessage(OrganizationPermissionCodes.BillingManage),
            exception.Message);
        permissions.Verify(service => service.HasPermissionAsync(
            organization.OwnerUserId,
            organization.Id,
            OrganizationPermissionCodes.BillingManage), Times.Once);
    }

    private ServiceProvider CreateService(
        OpenCRMContext context,
        IOrganizationPermissionService? organizationPermissionService = null)
    {
        return fixture.CreateServices(
            context,
            fixture.CreateDefaultsInitializer().Object,
            organizationPermissionService: organizationPermissionService);
    }
}
