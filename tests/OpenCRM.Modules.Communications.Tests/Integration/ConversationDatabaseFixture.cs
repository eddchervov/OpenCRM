using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Migrations;
using OpenCRM.Modules.Communications;
using OpenCRM.Modules.Communications.Contracts;
using OpenCRM.Services.Interfaces.FileStorages;
using OpenCRM.Services.Interfaces.Organizations;
using Moq;
using Testcontainers.PostgreSql;

namespace OpenCRM.Modules.Communications.Tests.Integration;

[CollectionDefinition(Name)]
public sealed class ConversationDatabaseCollection : ICollectionFixture<ConversationDatabaseFixture>
{
    public const string Name = "Communications PostgreSQL database";
}

public sealed class ConversationDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:15-alpine").Build();

    public OpenCRMContext CreateContext(IInterceptor? interceptor = null, bool enableRetries = false)
    {
        var options = new DbContextOptionsBuilder<OpenCRMContext>()
            .UseNpgsql(this._postgres.GetConnectionString(), database =>
            {
                database.MigrationsAssembly(typeof(AssemblyMarker).Assembly.GetName().Name);
                if (enableRetries)
                {
                    database.EnableRetryOnFailure(2, TimeSpan.FromMilliseconds(1), null);
                }
            })
            .AddInterceptors(interceptor is null ? [] : [interceptor])
            .Options;

        return new OpenCRMContext(options);
    }

    public ServiceProvider CreateServices(
        OpenCRMContext context,
        IReadOnlySet<string>? deniedPermissions = null,
        Mock<IFileStorageService>? fileStorage = null,
        bool hasClientMessagingFeature = true)
    {
        var permissions = new Mock<IOrganizationPermissionService>();
        var subscriptions = new Mock<IOrganizationSubscriptionService>();
        permissions.Setup(service => service.HasPermissionAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .ReturnsAsync((string _, Guid _, string code) => !(deniedPermissions?.Contains(code) ?? false));
        subscriptions.Setup(service => service.HasFeatureAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .ReturnsAsync((string _, Guid _, string code) => code != Helpers.FeatureCodes.ClientMessaging || hasClientMessagingFeature);
        subscriptions.Setup(service => service.GetWorkspaceAccessAsync(It.IsAny<string>(), It.IsAny<Guid>()))
            .ReturnsAsync(new OpenCRM.Services.Interfaces.Organizations.Dtos.OrganizationWorkspaceAccessDto());
        subscriptions.Setup(service => service.EnsureWorkspaceCanReadAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns((string _, Guid _, string code) => deniedPermissions?.Contains(code) == true
                ? Task.FromException(new UnauthorizedAccessException())
                : Task.CompletedTask);
        subscriptions.Setup(service => service.EnsureWorkspaceCanWriteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns((string _, Guid _, string code) => deniedPermissions?.Contains(code) == true
                ? Task.FromException(new UnauthorizedAccessException())
                : Task.CompletedTask);
        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddSingleton(permissions.Object);
        services.AddSingleton(subscriptions.Object);
        services.AddSingleton(fileStorage?.Object ?? new Mock<IFileStorageService>().Object);
        services.AddSingleton<IDataProtectionProvider, EphemeralDataProtectionProvider>();
        services.AddLogging();
        services.AddOpenCRMCommunications();

        return services.BuildServiceProvider();
    }

    public async Task<(Organization Organization, ApplicationUser Owner, ApplicationUser Employee)> CreateOrganizationAsync(
        OpenCRMContext context,
        string name = "Проверка переписки")
    {
        var owner = CreateUser("owner");
        var employee = CreateUser("employee");
        var organization = new Organization
        {
            Name = name,
            PrimaryScenarioCode = OrganizationTypeCodes.Detailing,
            OwnerUser = owner,
            Members =
            [
                new OrganizationMember { User = owner, RoleCode = OrganizationRoleCodes.Owner },
                new OrganizationMember { User = employee, RoleCode = OrganizationRoleCodes.Employee }
            ]
        };
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();

        return (organization, owner, employee);
    }

    public static OrganizationTelegramBot AddBot(Organization organization, long botId, string? username = null) => new()
    {
        OrganizationId = organization.Id,
        Username = username ?? $"studio_{botId}_bot",
        ProtectedToken = "protected-token",
        ProtectedWebhookSecret = "protected-secret",
        TelegramBotId = botId,
        IsEnabled = true
    };

    public async Task InitializeAsync()
    {
        await this._postgres.StartAsync();
        await using var context = this.CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await this._postgres.DisposeAsync();

    private static ApplicationUser CreateUser(string prefix)
    {
        var suffix = Guid.NewGuid().ToString("N");
        return new ApplicationUser
        {
            UserName = $"{prefix}-{suffix}",
            Email = $"{prefix}-{suffix}@example.test"
        };
    }
}
