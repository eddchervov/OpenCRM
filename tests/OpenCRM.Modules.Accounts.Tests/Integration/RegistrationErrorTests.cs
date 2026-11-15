using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Accounts;
using OpenCRM.Modules.Accounts.Users;
using OpenCRM.Services.Interfaces.Common;
using OpenCRM.Services.Interfaces.Emails;
using OpenCRM.Services.Interfaces.Notifications;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Services.Interfaces.Users;
using OpenCRM.WebApp.Controllers.Auth;
using OpenCRM.WebApp.Models.Auth;
using OpenCRM.WebApp.Infrastructure.Security;
using Microsoft.Extensions.Caching.Memory;

namespace OpenCRM.Modules.Accounts.Tests.Integration;

public sealed class RegistrationErrorTests(AccountsDatabaseFixture fixture) : IClassFixture<AccountsDatabaseFixture>
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RegistrationLogsOriginalExceptionAndDoesNotExposeItOnForm(bool invalidOperation)
    {
        await using var context = fixture.CreateContext();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddMemoryCache();
        services.AddAuthentication();
        services.AddSingleton(context);
        services.AddIdentityCore<ApplicationUser>()
            .AddEntityFrameworkStores<OpenCRMContext>()
            .AddSignInManager();
        await using var provider = services.BuildServiceProvider();
        Exception failure = invalidOperation
            ? new InvalidOperationException("NpgsqlRetryingExecutionStrategy; Host=private-database")
            : new DbUpdateException("SQL INSERT; internal-table; server-path");
        var unitOfWork = new Mock<IApplicationUnitOfWork>();
        unitOfWork.Setup(service => service.ExecuteInTransactionAsync(It.IsAny<Func<Task>>()))
            .ThrowsAsync(failure);
        var logger = new Mock<ILogger<AuthController>>();
        var notifications = new Mock<IEmailNotificationService>(MockBehavior.Strict);
        var url = new Mock<IUrlHelper>();
        url.Setup(helper => helper.Content("~/")).Returns("/");
        var controller = new AuthController(
            provider.GetRequiredService<SignInManager<ApplicationUser>>(),
            provider.GetRequiredService<UserManager<ApplicationUser>>(),
            notifications.Object,
            new EmailConfirmationDelivery(provider.GetRequiredService<IMemoryCache>(), TimeProvider.System),
            Mock.Of<IUsersService>(),
            Mock.Of<IPersonalSubscriptionQueryService>(),
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
            logger.Object,
            provider.GetRequiredService<IDataProtectionProvider>(),
            Mock.Of<IAntiforgery>(),
            new AccountRegistrationService(
                provider.GetRequiredService<UserManager<ApplicationUser>>(),
                unitOfWork.Object,
                Mock.Of<ISiteNotificationPublisher>(),
                context),
            Mock.Of<IAccountDeletionService>(),
            Mock.Of<IEmailCodeLoginService>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = provider }
            },
            TempData = Mock.Of<ITempDataDictionary>(),
            Url = url.Object
        };
        var model = new RegisterModel
        {
            Email = $"registration-error-{Guid.NewGuid():N}@example.com",
            Password ="Registration-test-123!",
            PersonalDataProcessingConsent = true
        };

        var response = Assert.IsType<ViewResult>(await controller.Register(model, null));

        Assert.Same(model, response.Model);
        var error = Assert.Single(controller.ModelState.Values.SelectMany(value => value.Errors));
        Assert.Equal("Не удалось завершить регистрацию. Попробуйте ещё раз чуть позже.", error.ErrorMessage);
        Assert.Null(error.Exception);
        Assert.False(await context.Users.AnyAsync(user => user.Email == model.Email));
        notifications.VerifyNoOtherCalls();
        logger.Verify(
            log => log.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, type) => state.ToString()!.Contains("Ошибка при регистрации")),
                failure,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
