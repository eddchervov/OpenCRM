using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Modules.Organizations.Contracts.Dtos;
using OpenCRM.Modules.Payments.Contracts;
using OpenCRM.Services.Interfaces.Subscriptions;
using OpenCRM.Services.Interfaces.Subscriptions.Dtos;
using OpenCRM.WebApp.Controllers.Subscriptions;
using OpenCRM.WebApp.Models.Subscriptions;

namespace OpenCRM.Infrastructure.Tests.Unit;

public sealed class SubscriptionPageTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public async Task IndexShowsPublicCatalogRegardlessOfAuthentication(bool isAuthenticated, int organizationCount)
    {
        const string userId = "catalog-user";
        var catalog = new Mock<IPersonalSubscriptionCatalogQueryService>(MockBehavior.Strict);
        var businessPlan = new SubscriptionPlanDto
        {
            Id = 1,
            Name = "Бизнес",
            PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Business
        };

        catalog.Setup(service => service.GetCatalogPlansAsync(null)).ReturnsAsync(new[]
        {
            businessPlan,
            new SubscriptionPlanDto { Id = 2, IsDemoPlan = true, PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Business },
            new SubscriptionPlanDto { Id = 3, PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Personal }
        });

        var organizations = Enumerable.Range(0, organizationCount)
            .Select(_ => new OrganizationDto { Id = Guid.NewGuid(), PrimaryScenarioCode = OrganizationTypeCodes.Detailing })
            .ToList();

        var organizationService = new Mock<IOrganizationQueryService>(MockBehavior.Strict);
        organizationService.Setup(service => service.GetOverviewAsync(userId))
            .ReturnsAsync(new TeamOverviewDto { Organizations = organizations });

        var userManager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);

        userManager.Setup(manager => manager.GetUserId(It.IsAny<ClaimsPrincipal>())).Returns(userId);
        using var manager = userManager.Object;
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(isAuthenticated
                ? new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "Test")
                : new ClaimsIdentity())
        };

        var controller = new SubscriptionController(
            Mock.Of<ISubscriptionService>(MockBehavior.Strict),
            Mock.Of<IPersonalSubscriptionCheckoutService>(MockBehavior.Strict),
            catalog.Object,
            manager,
            organizationService.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
            TempData = new TempDataDictionary(context, Mock.Of<ITempDataProvider>())
        };

        var result = Assert.IsType<ViewResult>(await controller.Index());
        var model = Assert.IsType<SubscriptionViewModel>(result.Model);
        Assert.Same(businessPlan, Assert.Single(Assert.Single(model.BusinessPlanGroups).Plans));
        Assert.Null(model.CurrentSubscription);
        Assert.False(model.CanUseTestSubscriptions);
        catalog.Verify(service => service.GetCatalogPlansAsync(null), Times.Once);
        catalog.VerifyNoOtherCalls();
        organizationService.Verify(service => service.GetOverviewAsync(userId),
            isAuthenticated ? Times.Once() : Times.Never());

        if (!isAuthenticated)
        {
            return;
        }

        var expectedUrl = organizationCount switch
        {
            0 => "/organizations/create",
            1 => $"/detailing/{organizations[0].Id:D}#billing",
            _ => "/organizations"
        };

        Assert.Equal(expectedUrl, model.BusinessPlansTargetUrl);
    }
}
