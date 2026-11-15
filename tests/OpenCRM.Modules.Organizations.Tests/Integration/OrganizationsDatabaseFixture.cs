using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using OpenCRM.Domain;
using OpenCRM.Modules.Notifications.Emails;
using OpenCRM.Modules.Notifications;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Migrations;
using OpenCRM.Modules.Organizations;
using OpenCRM.Modules.Organizations.Invitations;
using OpenCRM.Services.Interfaces.Notifications;
using OpenCRM.Services.Interfaces.Organizations;
using Testcontainers.PostgreSql;

namespace OpenCRM.Modules.Organizations.Tests.Integration;

[CollectionDefinition(Name)]
public sealed class OrganizationsDatabaseCollection : ICollectionFixture<OrganizationsDatabaseFixture>
{
    public const string Name = "Organizations PostgreSQL database";
}

public sealed class OrganizationsDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:15-alpine").Build();

    public OpenCRMContext CreateContext(params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<OpenCRMContext>()
            .UseNpgsql(this._postgres.GetConnectionString(), database =>
            {
                database.MigrationsAssembly(typeof(AssemblyMarker).Assembly.GetName().Name);
                database.EnableRetryOnFailure();
            })
            .AddInterceptors(interceptors)
            .Options;

        return new OpenCRMContext(options);
    }

    public async Task<Organization> CreateOrganizationAsync(
        OpenCRMContext context,
        string? name = null)
    {
        var owner = new OpenCRM.Domain.Identity.ApplicationUser
        {
            Email = $"owner-{Guid.NewGuid():N}@example.test"
        };
        var organization = new Organization
        {
            Name = name ?? "Организация для приглашений",
            PrimaryScenarioCode = "detailing",
            OwnerUser = owner,
            Members = [new OrganizationMember { User = owner, RoleCode = OrganizationRoleCodes.Owner }]
        };
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();

        return organization;
    }

    public async Task<OrganizationMember> AddMemberAsync(
        OpenCRMContext context,
        Organization organization,
        OpenCRM.Domain.Identity.ApplicationUser user,
        string roleCode = OrganizationRoleCodes.Employee)
    {
        var member = new OrganizationMember
        {
            OrganizationId = organization.Id,
            User = user,
            RoleCode = roleCode
        };
        context.OrganizationMembers.Add(member);
        await context.SaveChangesAsync();

        return member;
    }

    internal OrganizationInvitationCommandService CreateInvitationCommandService(
        OpenCRMContext context,
        IOrganizationSubscriptionService? subscriptions = null,
        IOrganizationAuditService? auditService = null,
        ISiteNotificationPublisher? publisher = null,
        IConfiguration? configuration = null)
    {
        var subscriptionMock = OrganizationInvitationTestServices.CreateSubscription();

        return new OrganizationInvitationCommandService(
            context,
            subscriptions ?? subscriptionMock.Object,
            auditService ?? new OrganizationAuditService(context),
            new OrganizationAccess(context),
            new OrganizationTransaction(context),
            new OrganizationInvitationNotifications(context, publisher ?? new DatabaseSiteNotificationPublisher(context)),
            new OrganizationInvitationEmail(new EmailSender(context), configuration ?? new ConfigurationBuilder().Build()));
    }

    internal OrganizationInvitationQueryService CreateInvitationQueryService(OpenCRMContext context) =>
        new(context, new OrganizationScenarioReader(context));

    public async Task InitializeAsync()
    {
        await this._postgres.StartAsync();
        await using var context = this.CreateContext();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => this._postgres.DisposeAsync().AsTask();

    private sealed class DatabaseSiteNotificationPublisher(OpenCRMContext context) : ISiteNotificationPublisher
    {
        public Task AddAsync(
            string typeCode,
            string recipientUserId,
            int audienceMask,
            string eventKey,
            IReadOnlyDictionary<string, string> values) =>
            SiteNotificationPublisher.AddAsync(context, typeCode, recipientUserId, audienceMask, eventKey, values);

        public Task AddRegistrationNotificationsAsync(OpenCRM.Domain.Identity.ApplicationUser user) =>
            SiteNotificationPublisher.AddRegistrationNotificationsAsync(context, user);
    }
}
