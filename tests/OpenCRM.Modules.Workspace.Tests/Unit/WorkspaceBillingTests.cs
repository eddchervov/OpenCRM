using Helpers;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Planning.Contracts;
using OpenCRM.Modules.Catalog.Contracts;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Modules.Organizations.Contracts.Dtos;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Services.Interfaces.Features.Dtos;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using OpenCRM.Services.Interfaces.Subscriptions.Dtos;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Inventory.Contracts;
using OpenCRM.WebApp.Controllers.Api.V1.Organizations;
using OpenCRM.WebApp.Infrastructure.Api;
using OpenCRM.WebApp.Models.Organizations;

namespace OpenCRM.Modules.Workspace.Tests.Unit;

public sealed class WorkspaceBillingTests
{
    private readonly ApplicationUser _user = new();
    private readonly OrganizationDto _organization = new() { Id = Guid.NewGuid(), CanManageBilling = true };
    private readonly Mock<IOrganizationSubscriptionService> _subscriptions = new();
    private readonly Mock<IOrganizationQueryService> _organizations = new();
    private readonly Mock<IOrganizationPermissionService> _permissions = new();
    private readonly Mock<ILogger<OrganizationWorkspaceApiController>> _logger = new();
    private readonly OrganizationWorkspaceApiController _controller;

    public WorkspaceBillingTests()
    {
        var users = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), Options.Create(new IdentityOptions()),
            Mock.Of<IPasswordHasher<ApplicationUser>>(), Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(), Mock.Of<ILookupNormalizer>(),
            new IdentityErrorDescriber(), Mock.Of<IServiceProvider>(), Mock.Of<ILogger<UserManager<ApplicationUser>>>());
        users.Setup(service => service.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(this._user);
        this._organizations.Setup(service => service.GetOverviewAsync(this._user.Id))
            .ReturnsAsync(new TeamOverviewDto { Organizations = [this._organization] });
        this._controller = new OrganizationWorkspaceApiController(
            users.Object, this._organizations.Object,
            Mock.Of<IOrganizationProfileCommandService>(), Mock.Of<IOrganizationArchiveService>(),
            Mock.Of<IOrganizationMembershipService>(), Mock.Of<IOrganizationInvitationCommandService>(), this._subscriptions.Object, Mock.Of<IOrganizationSubscriptionCheckoutService>(),
            this._permissions.Object, Mock.Of<IWorkspaceOverviewService>(), Mock.Of<IOrganizationWorkspaceShellService>(),
            Mock.Of<IOrganizationWorkspaceQueryService>(), Mock.Of<IOrganizationWikiContentProvider>(), Mock.Of<IVehicleOptionQueryService>(), Mock.Of<ISchedulerQueryService>(), Mock.Of<ISchedulerTaskCommandService>(), Mock.Of<ISchedulerRecurrenceService>(),
            Mock.Of<ICatalogQueryService>(), Mock.Of<ICatalogCommandService>(), Mock.Of<IInventoryQueryService>(), Mock.Of<IStockCommandService>(),
            Mock.Of<ISystemClock>(), this._logger.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    TraceIdentifier = "billing-test-trace",
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "System")], "test"))
                }
            }
        };
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SystemRoleAndTestFlagDoNotBypassOrganizationPermission(bool testAccount)
    {
        this._user.CanUseTestSubscriptions = testAccount;
        this._organization.CanManageBilling = false;

        var result = Assert.IsType<ObjectResult>(await this._controller.LoadWorkspaceBilling(this._organization.Id));

        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        var response = Assert.IsType<ApiErrorResponse>(result.Value);
        Assert.Equal("forbidden", response.ErrorCode);
        Assert.Contains("владельцу", response.Message);
        Assert.Equal("billing-test-trace", response.TraceId);
        this._subscriptions.VerifyNoOtherCalls();
        this.VerifyLog(LogLevel.Warning, null);
    }

    [Fact]
    public async Task BillingRemainsAvailableToAuthorizedMemberWithExpiredSubscription()
    {
        this._subscriptions.Setup(service => service.GetAvailablePlansAsync(this._user.Id, this._organization.Id, false))
            .ReturnsAsync(new List<SubscriptionPlanDto> { new() { Id = 1, Name = "Бизнес", IsDemoPlan = false } });
        this._subscriptions.Setup(service => service.GetWorkspaceAccessAsync(this._user.Id, this._organization.Id))
            .ReturnsAsync(new OrganizationWorkspaceAccessDto { IsReadOnly = true, HasActiveSubscription = false });
        this._subscriptions.Setup(service => service.GetFeatureSnapshotAsync(this._user.Id, this._organization.Id))
            .ReturnsAsync(new FeatureAccessSnapshotDto());

        var result = await this._controller.LoadWorkspaceBilling(this._organization.Id);

        Assert.IsType<OkObjectResult>(result);
        this._logger.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BillingFailureLogsOriginalExceptionAndReturnsSafeServerError(bool invalidOperation)
    {
        Exception failure = invalidOperation
            ? new InvalidOperationException("SQL; private-database; secret")
            : new IOException("internal-path; secret");
        this._subscriptions.Setup(service => service.CheckDemoAvailabilityAsync(this._user.Id, this._organization.Id))
            .ThrowsAsync(failure);

        var result = Assert.IsType<ObjectResult>(await this._controller.LoadWorkspaceBilling(this._organization.Id));

        Assert.Equal(StatusCodes.Status500InternalServerError, result.StatusCode);
        var response = Assert.IsType<ApiErrorResponse>(result.Value);
        Assert.Equal("server_error", response.ErrorCode);
        Assert.DoesNotContain("secret", response.Message);
        Assert.Equal("billing-test-trace", response.TraceId);
        this.VerifyLog(LogLevel.Error, failure);
    }

    [Fact]
    public async Task OrganizationOutsideMembershipIsNotReturned()
    {
        this._organizations.Setup(service => service.GetOverviewAsync(this._user.Id)).ReturnsAsync(new TeamOverviewDto());

        var result = await this._controller.LoadWorkspaceBilling(this._organization.Id);

        Assert.IsType<NotFoundResult>(result);
        this._subscriptions.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false, true, false, false, false, true)]
    [InlineData(true, true, true, true, true, true)]
    [InlineData(true, false, false, false, false, true)]
    public async Task WorkspaceMemberCapabilitiesMatchFeatureAndRolePermissions(
        bool membersFeatureEnabled,
        bool hasManagementPermissions,
        bool canManageMembers,
        bool canInviteMembers,
        bool canManageRoles,
        bool canRemoveMembers)
    {
        this._organization.CanManageMembers = hasManagementPermissions;
        this._permissions.Setup(service => service.HasPermissionAsync(
                this._user.Id, this._organization.Id, OrganizationPermissionCodes.MembersView))
            .ReturnsAsync(true);
        this._permissions.Setup(service => service.HasPermissionAsync(
                this._user.Id, this._organization.Id, OrganizationPermissionCodes.MembersInvite))
            .ReturnsAsync(hasManagementPermissions);
        this._permissions.Setup(service => service.HasPermissionAsync(
                this._user.Id, this._organization.Id, OrganizationPermissionCodes.MembersRolesManage))
            .ReturnsAsync(hasManagementPermissions);
        this._permissions.Setup(service => service.HasPermissionAsync(
                this._user.Id, this._organization.Id, OrganizationPermissionCodes.MembersRemove))
            .ReturnsAsync(true);
        this._subscriptions.Setup(service => service.EnsureWorkspaceCanReadAsync(
                this._user.Id, this._organization.Id, OrganizationPermissionCodes.MembersView))
            .Returns(Task.CompletedTask);
        this._subscriptions.Setup(service => service.GetWorkspaceAccessAsync(this._user.Id, this._organization.Id))
            .ReturnsAsync(new OrganizationWorkspaceAccessDto());
        this._subscriptions.Setup(service => service.GetFeatureSnapshotAsync(this._user.Id, this._organization.Id))
            .ReturnsAsync(new FeatureAccessSnapshotDto
            {
                Features =
                [
                    new FeatureValueDto { Code = FeatureCodes.MembersModule, BoolValue = membersFeatureEnabled }
                ]
            });

        var result = Assert.IsType<OkObjectResult>(await this._controller.LoadWorkspaceMembers(this._organization.Id));
        var response = result.Value!;
        Assert.Equal(canManageMembers, response.GetType().GetProperty("CanManageMembers")!.GetValue(response));
        Assert.Equal(canInviteMembers, response.GetType().GetProperty("CanInviteMembers")!.GetValue(response));
        Assert.Equal(canManageRoles, response.GetType().GetProperty("CanManageRoles")!.GetValue(response));
        Assert.Equal(canRemoveMembers, response.GetType().GetProperty("CanRemoveMembers")!.GetValue(response));
    }

    [Theory]
    [InlineData("Эта возможность доступна в PRO.")]
    [InlineData("В этой организации у вас доступ только на просмотр.")]
    [InlineData("Нет доступа к изменению прав ролей.")]
    public async Task RolePermissionsCannotBeChangedWhenWorkspaceWriteAccessIsDenied(string reason)
    {
        var exception = new InvalidOperationException(reason);
        this._subscriptions.Setup(service => service.EnsureWorkspaceCanWriteAsync(
                this._user.Id, this._organization.Id, OrganizationPermissionCodes.MembersRolesManage))
            .ThrowsAsync(exception);

        var result = await this._controller.UpdateWorkspaceRolePermissions(
            this._organization.Id, OrganizationRoleCodes.Employee, new UpdateOrganizationRolePermissionsDto());

        Assert.IsType<ForbidResult>(result);
        this._permissions.Verify(service => service.UpdateRolePermissionsAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<UpdateOrganizationRolePermissionsDto>()), Times.Never);
        this.VerifyLog(LogLevel.Warning, exception);
    }

    [Fact]
    public async Task AllowedRolePermissionsUseTheRouteRoleAfterWorkspaceAccessCheck()
    {
        var dto = new UpdateOrganizationRolePermissionsDto
        {
            RoleCode = OrganizationRoleCodes.Owner,
            EnabledPermissionCodes = [OrganizationPermissionCodes.WorkspaceView]
        };

        var result = await this._controller.UpdateWorkspaceRolePermissions(
            this._organization.Id, OrganizationRoleCodes.Employee, dto);

        Assert.IsType<OkObjectResult>(result);
        this._subscriptions.Verify(service => service.EnsureWorkspaceCanWriteAsync(
            this._user.Id, this._organization.Id, OrganizationPermissionCodes.MembersRolesManage), Times.Once);
        this._permissions.Verify(service => service.UpdateRolePermissionsAsync(
            this._user.Id, this._organization.Id, It.Is<UpdateOrganizationRolePermissionsDto>(request =>
                request.RoleCode == OrganizationRoleCodes.Employee
                && request.EnabledPermissionCodes.SequenceEqual(dto.EnabledPermissionCodes))), Times.Once);
    }

    [Fact]
    public async Task RolePermissionsOutsideMembershipDoNotReachAccessOrMutationServices()
    {
        this._organizations.Setup(service => service.GetOverviewAsync(this._user.Id)).ReturnsAsync(new TeamOverviewDto());

        var result = await this._controller.UpdateWorkspaceRolePermissions(
            this._organization.Id, OrganizationRoleCodes.Employee, new UpdateOrganizationRolePermissionsDto());

        Assert.IsType<NotFoundResult>(result);
        this._subscriptions.VerifyNoOtherCalls();
        this._permissions.VerifyNoOtherCalls();
    }

    private void VerifyLog(LogLevel level, Exception? exception)
    {
        this._logger.Verify(logger => logger.Log(
            level, It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((state, type) => state.ToString()!.Contains("billing-test-trace")
                && state.ToString()!.Contains(this._organization.Id.ToString())
                && state.ToString()!.Contains(this._user.Id)),
            exception, It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }
}
