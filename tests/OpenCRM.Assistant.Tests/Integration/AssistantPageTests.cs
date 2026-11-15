using System.Security.Claims;
using Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Assistant;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Modules.Organizations.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.WebApp.Controllers;
using OpenCRM.WebApp.Infrastructure.Assistant;

namespace OpenCRM.Assistant.Tests.Integration;

public sealed class AssistantPageTests(AssistantIntegrationFixture fixture) : IClassFixture<AssistantIntegrationFixture>
{
    [Theory]
    [InlineData(OrganizationTypeCodes.Detailing, "detailing")]
    [InlineData(OrganizationTypeCodes.ServiceStation, "autoservice")]
    public async Task FreeOpensPreviewWithWorkspaceLinksWithoutAssistantGrant(string scenario, string path)
    {
        var organizationId = await fixture.CreateOrganizationAsync("page-free");
        await using var context = fixture.CreateContext();
        var organization = await context.Organizations.SingleAsync(item => item.Id == organizationId);
        organization.PrimaryScenarioCode = scenario;
        var grant = await context.Set<AssistantOrganizationAccess>().SingleAsync(item => item.OrganizationId == organizationId);
        grant.IsEnabled = false;
        await context.SaveChangesAsync();
        var (controller, subscriptions) = AssistantPageTests.CreateController(context, organization, fixture.Options, false);
        controller.Request.PathBase = "/opencrm";

        var result = Assert.IsType<ViewResult>(await controller.Index(organizationId, CancellationToken.None));

        Assert.Equal("~/Views/AiPilot/Index.cshtml", result.ViewName);
        Assert.Equal(true, result.ViewData["AssistantPreviewOnly"]);
        Assert.Equal(organization.Name, result.ViewData["AssistantOrganizationName"]);
        Assert.Equal($"/opencrm/{path}/{organizationId:D}", result.ViewData["AssistantWorkspaceUrl"]);
        Assert.Equal("geolocation=(), microphone=()", controller.Response.Headers["Permissions-Policy"]);
        Assert.Equal("no-store", controller.Response.Headers.CacheControl);
        subscriptions.Verify(service => service.EnsureWorkspaceCanReadAsync(
            organization.OwnerUserId, organizationId, OrganizationPermissionCodes.WorkspaceView), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProRequiresEnabledAssistantInsteadOfShowingSubscriptionPreview(bool enabled)
    {
        var organizationId = await fixture.CreateOrganizationAsync("page-pro");
        await using var context = fixture.CreateContext();
        var organization = await context.Organizations.SingleAsync(item => item.Id == organizationId);
        var grant = await context.Set<AssistantOrganizationAccess>().SingleAsync(item => item.OrganizationId == organizationId);
        grant.WebEnabled = enabled;
        await context.SaveChangesAsync();
        var (controller, _) = AssistantPageTests.CreateController(context, organization, fixture.Options, true);

        var result = await controller.Index(organizationId, CancellationToken.None);

        if (!enabled)
        {
            Assert.IsType<NotFoundResult>(result);
            return;
        }

        Assert.Equal($"/autoservice/{organizationId:D}#assistant", Assert.IsType<RedirectResult>(result).Url);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PreviewRejectsMissingWorkspacePermissionAndUnsupportedScenario(bool denied)
    {
        var organizationId = await fixture.CreateOrganizationAsync("page-denied");
        await using var context = fixture.CreateContext();
        var organization = await context.Organizations.SingleAsync(item => item.Id == organizationId);
        if (!denied)
        {
            organization.PrimaryScenarioCode = OrganizationTypeCodes.Fleet;
            await context.SaveChangesAsync();
        }

        var (controller, subscriptions) = AssistantPageTests.CreateController(context, organization, fixture.Options, false);
        if (denied)
        {
            subscriptions.Setup(service => service.EnsureWorkspaceCanReadAsync(
                organization.OwnerUserId, organizationId, OrganizationPermissionCodes.WorkspaceView))
                .ThrowsAsync(new InvalidOperationException("Нет доступа к рабочему пространству."));
        }

        Assert.IsType<NotFoundResult>(await controller.Index(organizationId, CancellationToken.None));
        Assert.False(controller.ViewData.ContainsKey("AssistantOrganizationName"));
    }

    private static (AssistantController Controller, Mock<IOrganizationSubscriptionService> Subscriptions) CreateController(
        OpenCRMContext context,
        Organization organization,
        AssistantOptions options,
        bool hasAssistant)
    {
        var subscriptions = new Mock<IOrganizationSubscriptionService>();
        subscriptions.Setup(service => service.HasFeatureAsync(organization.OwnerUserId, organization.Id, FeatureCodes.Assistant))
            .ReturnsAsync(hasAssistant);
        subscriptions.Setup(service => service.EnsureWorkspaceCanReadAsync(
            organization.OwnerUserId, organization.Id, OrganizationPermissionCodes.WorkspaceView)).Returns(Task.CompletedTask);
        var organizations = new Mock<IOrganizationQueryService>();
        organizations.Setup(service => service.GetOverviewAsync(organization.OwnerUserId))
            .ReturnsAsync(new TeamOverviewDto
            {
                Organizations = [new OrganizationDto { Id = organization.Id, Name = organization.Name, PrimaryScenarioCode = organization.PrimaryScenarioCode }]
            });
        var users = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), Options.Create(new IdentityOptions()),
            Mock.Of<IPasswordHasher<ApplicationUser>>(), Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(), Mock.Of<ILookupNormalizer>(),
            new IdentityErrorDescriber(), Mock.Of<IServiceProvider>(), Mock.Of<ILogger<UserManager<ApplicationUser>>>());
        users.Setup(service => service.GetUserId(It.IsAny<ClaimsPrincipal>())).Returns(organization.OwnerUserId);
        var access = new AssistantAccessService(context, subscriptions.Object, Mock.Of<IOrganizationPermissionService>(), options);
        var controller = new AssistantController(access, users.Object, organizations.Object, subscriptions.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        return (controller, subscriptions);
    }
}
