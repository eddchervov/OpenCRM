using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Modules.Organizations.Bookings;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Organizations.Tests.Integration;

public sealed class OrganizationRegistrationTests
{
    [Fact]
    public void EachPortHasExactlyOneScopedImplementationAndPublicServicesKeepTheirVisibility()
    {
        var services = new ServiceCollection();
        Assert.Same(services, services.AddOpenCRMOrganizations());
        var ports = new[]
        {
            typeof(IOrganizationQueryService), typeof(IOrganizationProfileCommandService), typeof(IOrganizationArchiveService),
            typeof(IOrganizationDataExportService),
            typeof(IOrganizationMembershipService), typeof(IOrganizationInvitationQueryService),
            typeof(IOrganizationInvitationCommandService), typeof(IOrganizationSetupService),
            typeof(IOrganizationAuditService), typeof(IOrganizationPermissionService), typeof(IOrganizationEmployeeService),
            typeof(IOrganizationShiftService), typeof(IOrganizationDetailingQuickBookingService),
            typeof(IOrganizationAutoServiceQuickBookingService), typeof(IOrganizationInquiryBookingService),
            typeof(IOrganizationRepeatVisitBookingService), typeof(IOrganizationTimeZoneService)
        };

        foreach (var port in ports)
        {
            var registration = Assert.Single(services, descriptor => descriptor.ServiceType == port);
            Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);
            Assert.Equal("OpenCRM.Modules.Organizations", registration.ImplementationType!.Assembly.GetName().Name);
        }

        var publicTypes = new[]
        {
            typeof(OrganizationsServiceCollectionExtensions), typeof(OrganizationAuditService),
            typeof(OpenCRM.Modules.Organizations.Export.OrganizationDataExportOptions),
            typeof(OrganizationPermissionService), typeof(OrganizationEmployeeService),
            typeof(OrganizationShiftService), typeof(OrganizationDetailingQuickBookingService),
            typeof(OrganizationAutoServiceQuickBookingService), typeof(OrganizationInquiryBookingService),
            typeof(OrganizationRepeatVisitBookingService)
        };

        Assert.Equal(publicTypes.OrderBy(type => type.Name),
            typeof(OrganizationsServiceCollectionExtensions).Assembly.GetExportedTypes().OrderBy(type => type.Name));
    }

    [Fact]
    public void ModuleAndContractsKeepDependencyBoundaries()
    {
        var dependencies = typeof(OrganizationsServiceCollectionExtensions).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(dependencies, dependency => dependency.Name is "OpenCRM.Infrastructure.Common" or "OpenCRM.WebApp"
            or "OpenCRM.Modules.Clients" or "OpenCRM.Modules.Vehicles" or "OpenCRM.Modules.Inventory" or "OpenCRM.Modules.Maintenance");
        var contracts = typeof(IOrganizationQueryService).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(contracts, dependency => dependency.Name is "OpenCRM.Infrastructure.Common" or "OpenCRM.WebApp"
            or "OpenCRM.Modules.Organizations" or "OpenCRM.Infrastructure.Data" or "OpenCRM.Services.Interfaces");
    }
}
