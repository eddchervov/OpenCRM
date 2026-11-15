using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.FileStorage;
using OpenCRM.Services.Interfaces.Media;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Maintenance.Tests.Integration;

public sealed class MediaWorkspaceAccessTests(MaintenanceDatabaseFixture fixture)
    : IClassFixture<MaintenanceDatabaseFixture>
{
    [Theory]
    [InlineData(OrganizationRoleCodes.Owner)]
    [InlineData(OrganizationRoleCodes.Viewer)]
    public async Task ReadChecksMembershipWithoutRequestingAdditionalPermissions(string roleCode)
    {
        await using var context = fixture.CreateContext();
        var organization = MediaWorkspaceAccessTests.CreateOrganization(roleCode);
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();
        var subscription = new Mock<IOrganizationSubscriptionService>(MockBehavior.Strict);
        using var services = new ServiceCollection()
            .AddSingleton(context)
            .AddSingleton(subscription.Object)
            .AddOpenCRMFileStorageInfrastructure()
            .BuildServiceProvider();
        var access = services.GetRequiredService<IMediaWorkspaceAccess>();

        await access.EnsureCanReadWorkspaceAsync(organization.OwnerUserId, organization.Id);

        subscription.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("archived")]
    [InlineData("deleted")]
    [InlineData("foreign")]
    [InlineData("missing")]
    public async Task ReadAndWriteRejectUnavailableWorkspaces(string condition)
    {
        await using var context = fixture.CreateContext();
        var organization = MediaWorkspaceAccessTests.CreateOrganization(OrganizationRoleCodes.Owner);
        organization.IsActive = condition != "inactive";
        organization.IsArchived = condition == "archived";
        organization.IsDeleted = condition == "deleted";
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();
        var subscription = new Mock<IOrganizationSubscriptionService>(MockBehavior.Strict);
        using var services = new ServiceCollection()
            .AddSingleton(context)
            .AddSingleton(subscription.Object)
            .AddOpenCRMFileStorageInfrastructure()
            .BuildServiceProvider();
        var access = services.GetRequiredService<IMediaWorkspaceAccess>();
        var userId = condition == "foreign" ? "foreign-user" : organization.OwnerUserId;
        var organizationId = condition == "missing" ? Guid.NewGuid() : organization.Id;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => access.EnsureCanReadWorkspaceAsync(userId, organizationId));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => access.EnsureCanWriteWorkspaceAsync(userId, organizationId));

        subscription.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ViewerCanReadButCannotWrite()
    {
        await using var context = fixture.CreateContext();
        var organization = MediaWorkspaceAccessTests.CreateOrganization(OrganizationRoleCodes.Viewer);
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();
        var subscription = new Mock<IOrganizationSubscriptionService>(MockBehavior.Strict);
        using var services = new ServiceCollection()
            .AddSingleton(context)
            .AddSingleton(subscription.Object)
            .AddOpenCRMFileStorageInfrastructure()
            .BuildServiceProvider();
        var access = services.GetRequiredService<IMediaWorkspaceAccess>();

        await access.EnsureCanReadWorkspaceAsync(organization.OwnerUserId, organization.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => access.EnsureCanWriteWorkspaceAsync(organization.OwnerUserId, organization.Id));

        subscription.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task WritePassesTheRequestedPermissionAndPersonalWorkspaceBypassesOrganizationChecks()
    {
        await using var context = fixture.CreateContext();
        var organization = MediaWorkspaceAccessTests.CreateOrganization(OrganizationRoleCodes.Owner);
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();
        var subscription = new Mock<IOrganizationSubscriptionService>(MockBehavior.Strict);
        subscription.Setup(service => service.EnsureWorkspaceCanWriteAsync(
                organization.OwnerUserId, organization.Id, OrganizationPermissionCodes.MaintenanceEdit))
            .Returns(Task.CompletedTask);
        subscription.Setup(service => service.EnsureWorkspaceCanWriteAsync(organization.OwnerUserId, organization.Id))
            .Returns(Task.CompletedTask);
        using var services = new ServiceCollection()
            .AddSingleton(context)
            .AddSingleton(subscription.Object)
            .AddOpenCRMFileStorageInfrastructure()
            .BuildServiceProvider();
        var access = services.GetRequiredService<IMediaWorkspaceAccess>();

        await access.EnsureCanWriteWorkspaceAsync(organization.OwnerUserId, organization.Id,
            OrganizationPermissionCodes.MaintenanceEdit);
        await access.EnsureCanWriteWorkspaceAsync(organization.OwnerUserId, organization.Id);
        await access.EnsureCanReadWorkspaceAsync("personal-user", null);
        await access.EnsureCanWriteWorkspaceAsync("personal-user", null, OrganizationPermissionCodes.MaintenanceEdit);

        subscription.Verify(service => service.EnsureWorkspaceCanWriteAsync(
            organization.OwnerUserId, organization.Id, OrganizationPermissionCodes.MaintenanceEdit), Times.Once);
        subscription.Verify(service => service.EnsureWorkspaceCanWriteAsync(
            organization.OwnerUserId, organization.Id), Times.Once);
        subscription.VerifyNoOtherCalls();
    }

    private static Organization CreateOrganization(string roleCode)
    {
        var user = new ApplicationUser();

        return new Organization
        {
            Name = "Доступ к фотографиям",
            OwnerUser = user,
            Members = [new OrganizationMember { User = user, RoleCode = roleCode }]
        };
    }
}
