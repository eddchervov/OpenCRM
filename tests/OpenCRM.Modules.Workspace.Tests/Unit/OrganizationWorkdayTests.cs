using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Planning.Contracts;
using OpenCRM.Modules.Catalog.Contracts;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Modules.Organizations.Contracts.Dtos;
using Helpers;
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
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Inventory.Contracts;
using OpenCRM.WebApp.Controllers.Api.V1.Organizations;
using OpenCRM.WebApp.Models.Organizations;

namespace OpenCRM.Modules.Workspace.Tests.Unit;

public sealed class OrganizationWorkdayTests
{
    private readonly ApplicationUser _user = new();
    private readonly OrganizationDto _organization = new()
    {
        Id = Guid.NewGuid(),
        PrimaryScenarioCode = OrganizationTypeCodes.ServiceStation
    };
    private readonly Mock<IOrganizationPermissionService> _permissions = new();
    private readonly Mock<IOrganizationWorkspaceQueryService> _queries = new();
    private readonly Mock<IOrganizationQueryService> _organizations = new();
    private readonly OrganizationWorkspaceApiController _controller;

    public OrganizationWorkdayTests()
    {
        var users = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(),
            Options.Create(new IdentityOptions()),
            Mock.Of<IPasswordHasher<ApplicationUser>>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            Mock.Of<ILookupNormalizer>(),
            new IdentityErrorDescriber(),
            Mock.Of<IServiceProvider>(),
            Mock.Of<ILogger<UserManager<ApplicationUser>>>());
        users.Setup(service => service.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(this._user);

        this._organizations.Setup(service => service.GetOverviewAsync(this._user.Id)).ReturnsAsync(
            new TeamOverviewDto { Organizations = [this._organization] });

        var subscriptions = new Mock<IOrganizationSubscriptionService>();
        subscriptions.Setup(service => service.GetFeatureSnapshotAsync(this._user.Id, this._organization.Id)).ReturnsAsync(
            new FeatureAccessSnapshotDto
            {
                Features =
                [
                    new() { Code = FeatureCodes.VehiclesModule, BoolValue = true },
                    new() { Code = FeatureCodes.ClientsModule, BoolValue = true }
                ]
            });

        this._permissions.Setup(service => service.HasPermissionAsync(
            this._user.Id,
            this._organization.Id,
            It.IsAny<string>())).ReturnsAsync(true);

        this._controller = new OrganizationWorkspaceApiController(
            users.Object,
            this._organizations.Object,
            Mock.Of<IOrganizationProfileCommandService>(),
            Mock.Of<IOrganizationArchiveService>(),
            Mock.Of<IOrganizationMembershipService>(),
            Mock.Of<IOrganizationInvitationCommandService>(),
            subscriptions.Object,
            Mock.Of<IOrganizationSubscriptionCheckoutService>(),
            this._permissions.Object,
            Mock.Of<IWorkspaceOverviewService>(), Mock.Of<IOrganizationWorkspaceShellService>(),
            this._queries.Object,
            Mock.Of<IOrganizationWikiContentProvider>(),
            Mock.Of<IVehicleOptionQueryService>(),
            Mock.Of<ISchedulerQueryService>(), Mock.Of<ISchedulerTaskCommandService>(), Mock.Of<ISchedulerRecurrenceService>(),
            Mock.Of<ICatalogQueryService>(), Mock.Of<ICatalogCommandService>(),
            Mock.Of<IInventoryQueryService>(), Mock.Of<IStockCommandService>(),
            Mock.Of<ISystemClock>(),
            Mock.Of<ILogger<OrganizationWorkspaceApiController>>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { TraceIdentifier = "organization-workday-trace" }
            }
        };
    }

    [Theory]
    [InlineData(OrganizationTypeCodes.Detailing)]
    [InlineData(OrganizationTypeCodes.ServiceStation)]
    [InlineData(OrganizationTypeCodes.Fleet)]
    public async Task WorkdayIsAvailableForEveryOperationalScenario(string scenarioCode)
    {
        this._organization.PrimaryScenarioCode = scenarioCode;
        var expected = new OrganizationWorkdayDto();
        var date = new DateOnly(2026, 9, 18);
        this._queries.Setup(service => service.GetWorkdayAsync(
            this._organization.Id,
            new DateTime(2026, 9, 18, 0, 0, 0, DateTimeKind.Utc),
            true,
            TimeZoneInfo.Utc)).ReturnsAsync(expected);

        var result = Assert.IsType<OkObjectResult>(
            await this._controller.LoadWorkspaceWorkday(this._organization.Id, date));

        Assert.Same(expected, result.Value);
    }

    [Fact]
    public async Task WorkdayDoesNotLoadWithoutMaintenancePermission()
    {
        this._permissions.Setup(service => service.HasPermissionAsync(
            this._user.Id,
            this._organization.Id,
            OrganizationPermissionCodes.MaintenanceView)).ReturnsAsync(false);

        var result = Assert.IsType<ObjectResult>(
            await this._controller.LoadWorkspaceWorkday(this._organization.Id, new DateOnly(2026, 9, 18)));

        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        this._queries.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(OrganizationTypeCodes.Detailing)]
    [InlineData(OrganizationTypeCodes.ServiceStation)]
    [InlineData(OrganizationTypeCodes.Fleet)]
    public async Task VisualizationUsesCompleteQueryAndRespectsClientPermission(string scenarioCode)
    {
        this._organization.PrimaryScenarioCode = scenarioCode;
        this._organization.TimeZoneId = "Asia/Novosibirsk";
        this._permissions.Setup(service => service.HasPermissionAsync(
            this._user.Id,
            this._organization.Id,
            OrganizationPermissionCodes.ClientsView)).ReturnsAsync(false);

        var expected = new OrganizationWorkdayDto();
        var date = new DateOnly(2026, 10, 9);
        this._queries.Setup(service => service.GetVisualizationAsync(
            this._organization.Id,
            new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc),
            false,
            It.Is<TimeZoneInfo>(zone => zone.Id == "Asia/Novosibirsk"))).ReturnsAsync(expected);

        var result = Assert.IsType<OkObjectResult>(
            await this._controller.LoadWorkspaceVisualization(this._organization.Id, date));

        Assert.Same(expected, result.Value);
    }

    [Fact]
    public async Task VisualizationDoesNotLoadWithoutMaintenancePermission()
    {
        this._permissions.Setup(service => service.HasPermissionAsync(
            this._user.Id,
            this._organization.Id,
            OrganizationPermissionCodes.MaintenanceView)).ReturnsAsync(false);

        var result = Assert.IsType<ObjectResult>(
            await this._controller.LoadWorkspaceVisualization(this._organization.Id, new DateOnly(2026, 10, 9)));

        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        this._queries.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task VisualizationDoesNotLoadAnotherOrganization()
    {
        Assert.IsType<NotFoundResult>(
            await this._controller.LoadWorkspaceVisualization(Guid.NewGuid(), new DateOnly(2026, 10, 9)));

        this._queries.VerifyNoOtherCalls();
    }
}
