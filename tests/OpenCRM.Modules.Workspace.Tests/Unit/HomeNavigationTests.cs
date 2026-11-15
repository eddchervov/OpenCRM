using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Modules.Organizations.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Home;
using OpenCRM.Services.Interfaces.Users;
using OpenCRM.WebApp.Controllers.Pages;
using OpenCRM.WebApp.Infrastructure;

namespace OpenCRM.Modules.Workspace.Tests.Unit;

public sealed class HomeNavigationTests
{
    private const string ActorUserId = "user-1";

    [Fact]
    public async Task IndexWithoutOrganizationsRedirectsToOrganizationEntry()
    {
        var controller = this.CreateController(
            this.OrganizationService([]));

        var result = await controller.Index();

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/organizations", redirect.Url);
    }

    [Fact]
    public async Task IndexWithoutAuthenticatedUserShowsNeutralHome()
    {
        var controller = this.CreateController(
            this.OrganizationService([]),
            authenticated: false);

        var result = await controller.Index();

        Assert.IsType<ViewResult>(result);
    }

    [Theory]
    [InlineData(OrganizationTypeCodes.Detailing, "/detailing/")]
    [InlineData(OrganizationTypeCodes.ServiceStation, "/autoservice/")]
    [InlineData(OrganizationTypeCodes.Fleet, "/fleet/")]
    public async Task IndexRedirectsToWorkspaceOfSingleOrganizationByType(string scenarioCode, string expectedUrlPrefix)
    {
        var organization = new OrganizationDto { Id = Guid.NewGuid(), PrimaryScenarioCode = scenarioCode };
        var controller = this.CreateController(
            this.OrganizationService([organization]));

        var result = await controller.Index();

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal(expectedUrlPrefix + organization.Id.ToString("D"), redirect.Url);
    }

    [Fact]
    public async Task IndexRedirectsToOrganizationSelectedByCookie()
    {
        var first = new OrganizationDto
        {
            Id = Guid.NewGuid(),
            PrimaryScenarioCode = OrganizationTypeCodes.Detailing
        };
        var second = new OrganizationDto
        {
            Id = Guid.NewGuid(),
            PrimaryScenarioCode = OrganizationTypeCodes.Fleet
        };
        var controller = this.CreateController(
            this.OrganizationService([first, second]),
            selectedOrganizationId: second.Id);

        var result = await controller.Index();

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/fleet/" + second.Id.ToString("D"), redirect.Url);
    }

    [Fact]
    public async Task IndexIgnoresForeignCookieAndUsesFirstOwnOrganization()
    {
        var own = new OrganizationDto
        {
            Id = Guid.NewGuid(),
            PrimaryScenarioCode = OrganizationTypeCodes.ServiceStation
        };
        var controller = this.CreateController(
            this.OrganizationService([own]),
            selectedOrganizationId: Guid.NewGuid());

        var result = await controller.Index();

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/autoservice/" + own.Id.ToString("D"), redirect.Url);
    }

    private IOrganizationQueryService OrganizationService(IReadOnlyList<OrganizationDto> organizations)
    {
        return Mock.Of<IOrganizationQueryService>(service =>
            service.GetOverviewAsync(ActorUserId) == Task.FromResult(new TeamOverviewDto { Organizations = organizations.ToList() }));
    }

    private HomeController CreateController(
        IOrganizationQueryService organizationService,
        Guid? selectedOrganizationId = null,
        bool authenticated = true)
    {
        var httpContext = new DefaultHttpContext();
        if (selectedOrganizationId.HasValue)
        {
            httpContext.Request.Headers.Cookie = $"{CurrentOrganizationCookie.Name}={selectedOrganizationId.Value:D}";
        }

        var controller = new HomeController(
            Mock.Of<IUsersService>(users =>
                users.RecordLastDateOfEntry(It.IsAny<string>()) == Task.CompletedTask),
            this.CreateUserManager(authenticated),
            organizationService);
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext, RouteData = new RouteData() };
        controller.Url = new UrlHelper(controller.ControllerContext);

        return controller;
    }

    private UserManager<ApplicationUser> CreateUserManager(bool authenticated = true)
    {
        var manager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(),
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            null,
            null,
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null!,
            NullLogger<UserManager<ApplicationUser>>.Instance);
        manager.Setup(userManager => userManager.GetUserId(It.IsAny<ClaimsPrincipal>()))
            .Returns(authenticated ? ActorUserId : null!);

        return manager.Object;
    }
}
