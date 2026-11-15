using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Modules.Support;
using OpenCRM.Modules.Support.Admin;
using OpenCRM.Modules.Support.Personal;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Home;

namespace OpenCRM.Modules.Support.Tests.Integration;

public sealed class SupportServiceRegistrationTests
{
    [Fact]
    public void AddOpenCRMSupport_RegistersBothServicesAsScoped()
    {
        var services = new ServiceCollection();

        services.AddOpenCRMSupport();

        SupportServiceRegistrationTests.AssertScopedService<ISupportMessageService, SupportMessageService>(services);
        SupportServiceRegistrationTests.AssertScopedService<IAdminSupportMessageService, AdminSupportMessageService>(services);
    }

    private static void AssertScopedService<TService, TImplementation>(IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        var descriptor = Assert.Single(services, item => item.ServiceType == typeof(TService));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.Equal(typeof(TImplementation), descriptor.ImplementationType);
    }
}
