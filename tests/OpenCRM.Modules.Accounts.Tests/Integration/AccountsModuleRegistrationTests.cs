using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Common;
using OpenCRM.Modules.Accounts;
using OpenCRM.Services.Interfaces.Common;
using OpenCRM.Services.Interfaces.Emails;
using OpenCRM.Services.Interfaces.Notifications;
using OpenCRM.Services.Interfaces.Users;

namespace OpenCRM.Modules.Accounts.Tests.Integration;

public sealed class AccountsModuleRegistrationTests(AccountsDatabaseFixture fixture) : IClassFixture<AccountsDatabaseFixture>
{
    [Fact]
    public async Task AllAccountServicesResolveWithHostDependenciesAndRespectTheirLifetimes()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddAuthentication();
        services.AddScoped(_ => fixture.CreateContext());
        services.AddIdentityCore<ApplicationUser>().AddEntityFrameworkStores<OpenCRM.Infrastructure.Data.OpenCRMContext>();
        services.AddScoped<IApplicationUnitOfWork, ApplicationUnitOfWork>();
        services.AddSingleton(Mock.Of<IEmailNotificationService>());
        services.AddSingleton(Mock.Of<ISiteNotificationPublisher>());
        services.AddOpenCRMAccounts();
        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType.Namespace?.StartsWith("OpenCRM.Services.Interfaces.AccountProfiles") == true);

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var scopedServices = new[]
        {
            typeof(IAccountRegistrationService), typeof(IAccountDeletionService), typeof(IEmailCodeLoginService)
        };

        foreach (var serviceType in scopedServices)
        {
            var service = scope.ServiceProvider.GetRequiredService(serviceType);
            Assert.Equal("OpenCRM.Modules.Accounts", service.GetType().Assembly.GetName().Name);
            Assert.Same(service, scope.ServiceProvider.GetRequiredService(serviceType));
        }

        var users = scope.ServiceProvider.GetRequiredService<IUsersService>();
        Assert.Equal("OpenCRM.Modules.Accounts", users.GetType().Assembly.GetName().Name);
        Assert.NotSame(users, scope.ServiceProvider.GetRequiredService<IUsersService>());
    }
}
