using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Infrastructure.Common;
using OpenCRM.Modules.Notifications;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Services.Interfaces.Common;

namespace OpenCRM.Infrastructure.Tests.Integration;

public sealed class ModuleRegistrationBoundaryTests
{
    [Fact]
    public void InfrastructureRegistrationDoesNotRegisterImplementationsOwnedByOtherOpenCRMLibraries()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddOpenCRMInfrastructure());
        Assert.Equal(new[] { typeof(IApplicationUnitOfWork), typeof(ISystemClock) }
            .OrderBy(type => type.FullName), services.Select(descriptor => descriptor.ServiceType).OrderBy(type => type.FullName));

        var opencrmImplementations = services
            .Where(descriptor => descriptor.ImplementationType?.Assembly.GetName().Name
                ?.StartsWith("OpenCRM.", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.NotEmpty(opencrmImplementations);
        Assert.All(opencrmImplementations, descriptor =>
            Assert.Equal("OpenCRM.Infrastructure.Common", descriptor.ImplementationType!.Assembly.GetName().Name));
    }

    [Fact]
    public void RepositoryRegistrationContainsOnlyDataImplementationsWithoutAddingDatabaseConfiguration()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddOpenCRMRepositories());

        Assert.NotEmpty(services);
        Assert.All(services, descriptor =>
            Assert.Equal("OpenCRM.Infrastructure.Data", descriptor.ImplementationType!.Assembly.GetName().Name));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(OpenCRMContext));
    }

    [Theory]
    [InlineData("MaxNotifications:Enabled")]
    [InlineData("TelegramOrganizationNotifications:Enabled")]
    public void NotificationRegistrationRetainsValidationForEnabledWorkers(string enabledKey)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [enabledKey] = "true"
        }).Build();
        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(() => services.AddOpenCRMNotifications(configuration));
    }
}
