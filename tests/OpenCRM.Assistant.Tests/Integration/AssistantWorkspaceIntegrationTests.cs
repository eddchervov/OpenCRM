using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Assistant;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Modules.Organizations.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.WebApp.Controllers;
using OpenCRM.WebApp.Controllers.AutoService;
using OpenCRM.WebApp.Controllers.Detailing;

namespace OpenCRM.Assistant.Tests.Integration;

public sealed class AssistantWorkspaceIntegrationTests(AssistantIntegrationFixture fixture) : IClassFixture<AssistantIntegrationFixture>
{
    [Theory]
    [InlineData(OrganizationTypeCodes.ServiceStation, "autoservice")]
    [InlineData(OrganizationTypeCodes.Detailing, "detailing")]
    public async Task DirectEntry_RedirectsIntoCorrectCrm(string scenarioCode, string workspace)
    {
        var organizationId = await fixture.CreateOrganizationAsync("assistant-workspace");
        var userId = await fixture.GetOwnerUserIdAsync(organizationId);
        await using var scope = fixture.CreateAssistant(new FakeAssistantActionExecutor(), new FakeRouterHandler([]));
        await scope.Context.Organizations.Where(item => item.Id == organizationId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.PrimaryScenarioCode, scenarioCode));
        var controller = this.CreateController(scope, userId, organizationId, scenarioCode);

        var result = Assert.IsType<RedirectResult>(await controller.Index(organizationId, CancellationToken.None));

        Assert.Equal($"/{workspace}/{organizationId:D}#assistant", result.Url);
    }

    [Fact]
    public async Task Embedded_UsesSameAccessChecksAndDisablesCaching()
    {
        var organizationId = await fixture.CreateOrganizationAsync("assistant-embedded");
        var userId = await fixture.GetOwnerUserIdAsync(organizationId);
        await using var scope = fixture.CreateAssistant(new FakeAssistantActionExecutor(), new FakeRouterHandler([]));
        var controller = this.CreateController(scope, userId, organizationId, OrganizationTypeCodes.ServiceStation);

        var result = Assert.IsType<ViewResult>(await controller.Embedded(organizationId, CancellationToken.None));

        Assert.Equal("~/Views/AiPilot/Index.cshtml", result.ViewName);
        Assert.Equal(true, controller.ViewData["AssistantEmbedded"]);
        Assert.Equal(organizationId, controller.ViewData["OrganizationId"]);
        Assert.Equal("no-store", controller.Response.Headers.CacheControl.ToString());
        Assert.Equal("geolocation=(), microphone=(self)", controller.Response.Headers["Permissions-Policy"].ToString());
        await scope.Context.Set<AssistantOrganizationAccess>().Where(item => item.OrganizationId == organizationId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.WebEnabled, false));

        Assert.IsType<NotFoundResult>(await controller.Embedded(organizationId, CancellationToken.None));
        Assert.IsType<NotFoundResult>(await controller.Index(organizationId, CancellationToken.None));
    }

    [Theory]
    [InlineData(OrganizationTypeCodes.ServiceStation, "autoservice")]
    [InlineData(OrganizationTypeCodes.Detailing, "detailing")]
    public async Task FreeEntry_ShowsPreviewAndRejectsEmbeddedExecution(string scenarioCode, string workspace)
    {
        var organizationId = await fixture.CreateOrganizationAsync("assistant-free-preview");
        var userId = await fixture.GetOwnerUserIdAsync(organizationId);
        await using var scope = fixture.CreateAssistant(new FakeAssistantActionExecutor(), new FakeRouterHandler([]), hasAssistantFeature: false);
        await scope.Context.Organizations.Where(item => item.Id == organizationId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.PrimaryScenarioCode, scenarioCode));
        var controller = this.CreateController(scope, userId, organizationId, scenarioCode, hasAssistantFeature: false);
        controller.Request.PathBase = "/opencrm";

        var result = Assert.IsType<ViewResult>(await controller.Index(organizationId, CancellationToken.None));

        Assert.Equal("~/Views/AiPilot/Index.cshtml", result.ViewName);
        Assert.Equal(true, controller.ViewData["AssistantPreviewOnly"]);
        Assert.Equal($"/opencrm/{workspace}/{organizationId:D}", controller.ViewData["AssistantWorkspaceUrl"]);
        Assert.Equal("no-store", controller.Response.Headers.CacheControl.ToString());
        Assert.Equal("geolocation=(), microphone=()", controller.Response.Headers["Permissions-Policy"].ToString());
        Assert.IsType<NotFoundResult>(await controller.Embedded(organizationId, CancellationToken.None));
    }

    [Fact]
    public async Task EntryAndFrame_RejectOrganizationMissingFromCurrentMembership()
    {
        var organizationId = await fixture.CreateOrganizationAsync("assistant-missing-member");
        var userId = await fixture.GetOwnerUserIdAsync(organizationId);
        await using var scope = fixture.CreateAssistant(new FakeAssistantActionExecutor(), new FakeRouterHandler([]));
        var controller = this.CreateController(scope, userId, Guid.NewGuid(), OrganizationTypeCodes.ServiceStation);

        Assert.IsType<NotFoundResult>(await controller.Index(organizationId, CancellationToken.None));
        Assert.IsType<NotFoundResult>(await controller.Embedded(organizationId, CancellationToken.None));
    }

    [Theory]
    [InlineData(OrganizationTypeCodes.ServiceStation)]
    [InlineData(OrganizationTypeCodes.Detailing)]
    public async Task CrmParent_AllowsSameOriginMicrophoneForAssistant(string scenarioCode)
    {
        var organizationId = Guid.NewGuid();
        var users = AssistantWorkspaceIntegrationTests.Users("workspace-user");
        var organizations = AssistantWorkspaceIntegrationTests.Organizations(organizationId, scenarioCode);
        Controller controller = scenarioCode == OrganizationTypeCodes.Detailing
            ? new DetailingClientController(users.Object, organizations.Object)
            : new AutoServiceClientController(users.Object, organizations.Object);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        var result = controller is DetailingClientController detailing
            ? await detailing.Index(organizationId)
            : await ((AutoServiceClientController)controller).Index(organizationId);

        Assert.IsType<ViewResult>(result);
        Assert.Equal("geolocation=(), microphone=(self)", controller.Response.Headers["Permissions-Policy"].ToString());
    }

    private AssistantController CreateController(AssistantTestScope scope, string userId, Guid organizationId, string scenarioCode, bool hasAssistantFeature = true)
    {
        var users = AssistantWorkspaceIntegrationTests.Users(userId);
        var organizations = AssistantWorkspaceIntegrationTests.Organizations(organizationId, scenarioCode);

        var subscriptions = new Mock<IOrganizationSubscriptionService>();
        subscriptions.Setup(service => service.HasFeatureAsync(userId, organizationId, Helpers.FeatureCodes.Assistant))
            .ReturnsAsync(hasAssistantFeature);

        return new AssistantController(scope.Access, users.Object, organizations.Object, subscriptions.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    private static Mock<UserManager<ApplicationUser>> Users(string userId)
    {
        var users = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        users.Setup(manager => manager.GetUserId(It.IsAny<ClaimsPrincipal>())).Returns(userId);
        users.Setup(manager => manager.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(new ApplicationUser { Id = userId });

        return users;
    }

    private static Mock<IOrganizationQueryService> Organizations(Guid organizationId, string scenarioCode)
    {
        var organizations = new Mock<IOrganizationQueryService>();
        organizations.Setup(service => service.GetOverviewAsync(It.IsAny<string>())).ReturnsAsync(new TeamOverviewDto
        {
            Organizations = [new OrganizationDto { Id = organizationId, Name = "Тестовая организация", PrimaryScenarioCode = scenarioCode }]
        });

        return organizations;
    }
}
