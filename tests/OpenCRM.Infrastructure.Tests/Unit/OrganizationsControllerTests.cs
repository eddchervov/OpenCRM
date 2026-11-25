using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Mvc.ViewFeatures.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Modules.Organizations.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Users;
using OpenCRM.WebApp.Controllers.Organizations;
using OpenCRM.WebApp.Infrastructure.Localization;
using OpenCRM.WebApp.Models.Organizations;

namespace OpenCRM.Infrastructure.Tests.Unit;

public sealed class OrganizationsControllerTests
{
    [Fact]
    public async Task CreateOrganizationRedirectsWithSerializableSuccessMessage()
    {
        const string userId = "test-owner";
        const string successKey = "organization.create.success";
        const string successMessage = "Организация создана.";
        var organizationId = Guid.NewGuid();

        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "Test"))
        };

        var userManager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);

        userManager.Setup(manager => manager.GetUserAsync(context.User)).ReturnsAsync(new ApplicationUser { Id = userId });
        using var manager = userManager.Object;

        var profile = new Mock<IOrganizationProfileCommandService>(MockBehavior.Strict);

        profile.Setup(service => service.CreateAsync(userId, It.Is<CreateOrganizationDto>(dto =>
                dto.PrimaryScenarioCode == OrganizationTypeCodes.Detailing && dto.CurrencyCode == CurrencyCodes.Eur
                && dto.TimeZoneId == "Asia/Novosibirsk")))
            .ReturnsAsync(organizationId);

        var localizer = new Mock<IStringLocalizer<LocalizationStrings>>(MockBehavior.Strict);
        localizer.Setup(text => text[successKey]).Returns(new LocalizedString(successKey, successMessage));

        var controller = new OrganizationsController(
            manager,
            Mock.Of<IUsersService>(MockBehavior.Strict),
            Mock.Of<IOrganizationQueryService>(MockBehavior.Strict),
            profile.Object,
            Mock.Of<IOrganizationArchiveService>(MockBehavior.Strict),
            Mock.Of<IOrganizationInvitationQueryService>(MockBehavior.Strict),
            Mock.Of<IOrganizationSubscriptionService>(MockBehavior.Strict),
            Mock.Of<IOrganizationTypeCatalog>(MockBehavior.Strict),
            NullLogger<OrganizationsController>.Instance,
            localizer.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
            TempData = new TempDataDictionary(context, Mock.Of<ITempDataProvider>())
        };

        var result = await controller.CreateOrganization(new CreateOrganizationInputModel
        {
            Name = "Студия",
            PrimaryScenarioCode = OrganizationTypeCodes.Detailing,
            CurrencyCode = CurrencyCodes.Eur,
            TimeZoneId = "Asia/Novosibirsk"
        });

        Assert.Equal($"/detailing/{organizationId:D}", Assert.IsType<RedirectResult>(result).Url);

        using var services = new ServiceCollection().AddControllersWithViews().Services.BuildServiceProvider();
        var serializer = services.GetRequiredService<TempDataSerializer>();
        var restoredData = serializer.Deserialize(serializer.Serialize(controller.TempData));

        Assert.Equal(successMessage, restoredData["Success"]);
        Assert.Equal("organization_created", restoredData["AnalyticsGoal"]);
    }
}
