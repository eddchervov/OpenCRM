using OpenCRM.Modules.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Services.Interfaces.Emails;
using OpenCRM.Services.Interfaces.Notifications;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Organizations.Tests.Integration;

internal static class OrganizationCommandHarness
{
    internal static ServiceProvider CreateServices(
        OpenCRMContext context,
        IOrganizationAuditService? audit = null,
        IOrganizationSubscriptionService? subscriptions = null,
        IOrganizationPermissionService? permissions = null,
        bool archivingEnabled = false)
    {
        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddSingleton(subscriptions ?? Mock.Of<IOrganizationSubscriptionService>());
        services.AddSingleton(Mock.Of<IEmailNotificationService>());
        services.AddSingleton(Mock.Of<ISiteNotificationPublisher>());
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Organizations:ArchivingEnabled"] = archivingEnabled.ToString() }).Build());
        services.AddOpenCRMCatalog();
        services.AddOpenCRMOrganizations();
        services.AddSingleton(permissions ?? Mock.Of<IOrganizationPermissionService>());
        services.AddSingleton(audit ?? new OrganizationAuditService(context));

        return services.BuildServiceProvider();
    }

    internal static async Task<OrganizationMember> AddMemberAsync(
        OpenCRMContext context,
        Organization organization,
        string role = OrganizationRoleCodes.Employee)
    {
        var member = new OrganizationMember
        {
            OrganizationId = organization.Id,
            User = new ApplicationUser { Email = $"member-{Guid.NewGuid():N}@example.test" },
            RoleCode = role
        };
        context.OrganizationMembers.Add(member);
        await context.SaveChangesAsync();

        return member;
    }

    internal static async Task<Organization> ReadAsync(OpenCRMContext context, Guid organizationId) =>
        await context.Organizations.AsNoTracking().Include(value => value.Members).SingleAsync(value => value.Id == organizationId);
}
