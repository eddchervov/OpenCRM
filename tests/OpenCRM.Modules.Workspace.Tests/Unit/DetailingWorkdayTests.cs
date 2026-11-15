using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Modules.Organizations.Contracts.Dtos;
using Helpers;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Services.Interfaces.Features.Dtos;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using OpenCRM.WebApp.Controllers.Api.V1.Detailing;

namespace OpenCRM.Modules.Workspace.Tests.Unit;

public sealed class DetailingWorkdayTests
{
    private readonly ApplicationUser _user = new();
    private readonly OrganizationDto _organization = new()
    {
        Id = Guid.NewGuid(), PrimaryScenarioCode = OrganizationTypeCodes.Detailing
    };
    private readonly Mock<IOrganizationPermissionService> _permissions = new();
    private readonly Mock<IOrganizationWorkspaceQueryService> _queries = new();
    private readonly Mock<IOrganizationQueryService> _organizations = new();
    private readonly Mock<ILogger<DetailingWorkspaceApiController>> _logger = new();
    private readonly FeatureAccessSnapshotDto _features = new()
    {
        Features = [new() { Code = FeatureCodes.VehiclesModule, BoolValue = true }, new() { Code = FeatureCodes.ClientsModule, BoolValue = true }]
    };
    private readonly DetailingWorkspaceApiController _controller;

    public DetailingWorkdayTests()
    {
        var users = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), Options.Create(new IdentityOptions()),
            Mock.Of<IPasswordHasher<ApplicationUser>>(), Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(), Mock.Of<ILookupNormalizer>(),
            new IdentityErrorDescriber(), Mock.Of<IServiceProvider>(), Mock.Of<ILogger<UserManager<ApplicationUser>>>());
        users.Setup(service => service.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(this._user);
        var organizationTypes = new Mock<IOrganizationTypeCatalog>();
        this._organizations.Setup(service => service.GetOverviewAsync(this._user.Id))
            .ReturnsAsync(new TeamOverviewDto { Organizations = [this._organization] });
        var subscriptions = new Mock<IOrganizationSubscriptionService>();
        subscriptions.Setup(service => service.GetFeatureSnapshotAsync(this._user.Id, this._organization.Id)).ReturnsAsync(this._features);
        this._permissions.Setup(service => service.HasPermissionAsync(this._user.Id, this._organization.Id, It.IsAny<string>())).ReturnsAsync(true);
        this._controller = new DetailingWorkspaceApiController(users.Object, Mock.Of<IAuthorizationService>(), organizationTypes.Object,
            this._organizations.Object, subscriptions.Object, this._permissions.Object,
            Mock.Of<IOrganizationWorkspaceShellService>(), this._queries.Object, this._logger.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { TraceIdentifier = "workday-trace" } }
        };
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CalendarDateAndClientPermissionArePassedToQuery(
        bool canViewClients)
    {
        this._permissions.Setup(service => service.HasPermissionAsync(this._user.Id, this._organization.Id, OrganizationPermissionCodes.ClientsView))
            .ReturnsAsync(canViewClients);
        var expected = new OrganizationWorkdayDto();
        var date = new DateOnly(2026, 9, 11);
        this._queries.Setup(service => service.GetWorkdayAsync(this._organization.Id,
            new DateTime(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc), canViewClients, TimeZoneInfo.Utc)).ReturnsAsync(expected);

        var result = Assert.IsType<OkObjectResult>(await this._controller.LoadWorkday(this._organization.Id, date));

        Assert.Same(expected, result.Value);
        this._queries.Verify(service => service.GetWorkdayAsync(this._organization.Id,
            It.Is<DateTime>(value => value.Kind == DateTimeKind.Utc && value.Day == 11), canViewClients, TimeZoneInfo.Utc), Times.Once);
    }

    [Theory]
    [InlineData("scenario")]
    [InlineData("membership")]
    [InlineData("permission")]
    [InlineData("module")]
    public async Task WorkdayDoesNotBypassAccessChecks(string restriction)
    {
        if (restriction == "scenario")
        {
            this._organization.PrimaryScenarioCode = OrganizationTypeCodes.ServiceStation;
        }
        if (restriction == "membership")
        {
    this._organizations.Setup(service => service.GetOverviewAsync(this._user.Id)).ReturnsAsync(new TeamOverviewDto());
        }
        if (restriction == "permission")
        {
    this._permissions.Setup(service => service.HasPermissionAsync(this._user.Id, this._organization.Id, OrganizationPermissionCodes.MaintenanceView)).ReturnsAsync(false);
        }
        if (restriction == "module")
        {
    this._features.Features.Clear();
        }

        var result = await this._controller.LoadWorkday(this._organization.Id, new DateOnly(2026, 9, 11));

        Assert.IsNotType<OkObjectResult>(result);
        this._queries.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InvalidDayDoesNotRunQuery()
    {
        Assert.IsType<BadRequestObjectResult>(await this._controller.LoadWorkday(this._organization.Id, default));
        Assert.IsType<BadRequestObjectResult>(await this._controller.LoadWorkday(this._organization.Id, DateOnly.MaxValue));
        this._queries.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UnexpectedQueryFailureIsLoggedAndDoesNotReturnDetails()
    {
        var failure = new InvalidOperationException("SQL internal details");
        this._queries.Setup(service => service.GetWorkdayAsync(this._organization.Id, It.IsAny<DateTime>(), true, TimeZoneInfo.Utc)).ThrowsAsync(failure);

        var result = Assert.IsType<ObjectResult>(await this._controller.LoadWorkday(this._organization.Id, new DateOnly(2026, 9, 11)));

        Assert.Equal(StatusCodes.Status500InternalServerError, result.StatusCode);
        Assert.DoesNotContain("SQL", System.Text.Json.JsonSerializer.Serialize(result.Value));
        this._logger.Verify(logger => logger.Log(LogLevel.Error, It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((state, type) => state.ToString()!.Contains("workday-trace")),
            failure, It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }
}
