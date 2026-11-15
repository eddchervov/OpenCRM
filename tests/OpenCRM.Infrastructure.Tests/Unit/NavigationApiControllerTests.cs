using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Admin.Dtos;
using OpenCRM.WebApp.Controllers.Admin.Api;

namespace OpenCRM.Infrastructure.Tests.Unit;

public sealed class NavigationApiControllerTests
{
    [Fact]
    public async Task CountsPassOnlyAuthorizedSectionsAndUtcBoundariesToService()
    {
        var authorization = new Mock<IAuthorizationService>();
        authorization.Setup(service => service.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(), It.IsAny<object?>(), It.IsAny<string>()))
            .ReturnsAsync((ClaimsPrincipal _, object? _, string policy) =>
                policy == AdminAuthorizationPolicies.ViewContent ? AuthorizationResult.Success() : AuthorizationResult.Failed());
        var navigation = new Mock<IAdminNavigationService>();
        var from = new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.FromHours(7));
        var to = from.AddDays(1);
        navigation.Setup(service => service.GetCountsAsync(from.UtcDateTime, to.UtcDateTime,
                It.Is<IReadOnlySet<string>>(permissions => permissions.SetEquals(new[] { AdminAuthorizationPolicies.ViewContent })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdminNavigationCountsDto { SupportMessages = 3 });
        var controller = new NavigationApiController(navigation.Object, authorization.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = Assert.IsType<OkObjectResult>(await controller.GetCounts(from, to, CancellationToken.None));

        Assert.Equal(3, Assert.IsType<AdminNavigationCountsDto>(result.Value).SupportMessages);
        navigation.VerifyAll();
    }

    [Fact]
    public async Task UnreadCountsUseAuthenticatedUserAndRejectFutureBaseline()
    {
        var authorization = new Mock<IAuthorizationService>();
        authorization.Setup(service => service.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(), It.IsAny<object?>(), It.IsAny<string>()))
            .ReturnsAsync(AuthorizationResult.Success());
        var navigation = new Mock<IAdminNavigationService>();
        var from = new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(1);
        navigation.Setup(service => service.GetUnreadCountsAsync(
                "admin-1",
                from.UtcDateTime,
                It.Is<IReadOnlySet<string>>(permissions => permissions.Count == 4),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdminNavigationCountsDto { Users = 5, AsOfUtc = from.UtcDateTime.AddHours(2) });
        var controller = CreateController(navigation.Object, authorization.Object, "admin-1");

        var response = Assert.IsType<OkObjectResult>(
            await controller.GetCounts(from, to, CancellationToken.None, unread: true));

        Assert.Equal(5, Assert.IsType<AdminNavigationCountsDto>(response.Value).Users);
        navigation.VerifyAll();

        var futureController = CreateController(navigation.Object, authorization.Object, "admin-1");
        var futureFrom = DateTimeOffset.UtcNow.AddDays(1);
        var futureTo = futureFrom.AddDays(1);

        Assert.IsType<BadRequestObjectResult>(
            await futureController.GetCounts(futureFrom, futureTo, CancellationToken.None, unread: true));
    }

    [Fact]
    public async Task ViewedMarkerRejectsPaymentsAndRequiresPermissionForTrackedSections()
    {
        var authorization = new Mock<IAuthorizationService>();
        authorization.Setup(service => service.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(), It.IsAny<object?>(), AdminAuthorizationPolicies.ViewUsers))
            .ReturnsAsync(AuthorizationResult.Success());
        var navigation = new Mock<IAdminNavigationService>();
        var viewedThroughUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        navigation.Setup(service => service.MarkSectionViewedAsync(
                "admin-1",
                AdminNavigationSections.Users,
                It.Is<DateTime>(value => value == new DateTime(value.Ticks - value.Ticks % 10, DateTimeKind.Utc)),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var controller = CreateController(navigation.Object, authorization.Object, "admin-1");

        var response = Assert.IsType<OkObjectResult>(await controller.MarkSectionViewed(
            AdminNavigationSections.Users,
            new NavigationApiController.MarkNavigationSectionViewedRequest { ViewedThroughUtc = viewedThroughUtc },
            CancellationToken.None));

        Assert.NotNull(response.Value);
        navigation.VerifyAll();

        var paymentsResponse = await controller.MarkSectionViewed(
            AdminNavigationSections.Payments,
            new NavigationApiController.MarkNavigationSectionViewedRequest { ViewedThroughUtc = viewedThroughUtc },
            CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(paymentsResponse);
        navigation.VerifyNoOtherCalls();

        var forbiddenAuthorization = new Mock<IAuthorizationService>();
        forbiddenAuthorization.Setup(service => service.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(), It.IsAny<object?>(), AdminAuthorizationPolicies.ViewUsers))
            .ReturnsAsync(AuthorizationResult.Failed());
        var forbiddenController = CreateController(navigation.Object, forbiddenAuthorization.Object, "admin-1");
        Assert.IsType<ForbidResult>(await forbiddenController.MarkSectionViewed(
            AdminNavigationSections.Users,
            new NavigationApiController.MarkNavigationSectionViewedRequest { ViewedThroughUtc = viewedThroughUtc },
            CancellationToken.None));
        navigation.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(26)]
    public async Task InvalidDayRangeDoesNotQueryCounts(int hours)
    {
        var navigation = new Mock<IAdminNavigationService>(MockBehavior.Strict);
        var controller = new NavigationApiController(navigation.Object, Mock.Of<IAuthorizationService>());
        var from = DateTimeOffset.UtcNow;

        Assert.IsType<BadRequestObjectResult>(await controller.GetCounts(from, from.AddHours(hours), CancellationToken.None));
        navigation.VerifyNoOtherCalls();
    }

    private static NavigationApiController CreateController(
        IAdminNavigationService navigation,
        IAuthorizationService authorization,
        string userId) =>
        new(navigation, authorization)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId)], "test"))
                }
            }
        };
}
