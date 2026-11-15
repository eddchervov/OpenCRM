using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Helpers;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Domain.Interfaces.Brands;
using OpenCRM.Domain.Interfaces.Models;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.Repositories;
using OpenCRM.Infrastructure.Migrations;
using OpenCRM.Modules.Catalog;
using OpenCRM.Modules.Planning;
using OpenCRM.Services.Interfaces.Organizations;
using Testcontainers.PostgreSql;

namespace OpenCRM.Modules.Planning.Tests.Integration;

[CollectionDefinition(Name)]
public sealed class PlanningDatabaseCollection : ICollectionFixture<PlanningDatabaseFixture>
{
    public const string Name = "Planning PostgreSQL database";
}

public sealed class PlanningDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:15-alpine").Build();

    public OpenCRMContext CreateContext(params IInterceptor[] interceptors) => new(new DbContextOptionsBuilder<OpenCRMContext>()
        .UseNpgsql(this._postgres.GetConnectionString(), database =>
        {
            database.MigrationsAssembly(typeof(AssemblyMarker).Assembly.GetName().Name);
            database.EnableRetryOnFailure();
        })
        .AddInterceptors(interceptors).Options);

    public async Task<Organization> CreateOrganizationAsync(OpenCRMContext context, string scenario = "detailing")
    {
        var owner = new ApplicationUser { Email = $"planning-{Guid.NewGuid():N}@example.test" };
        var organization = new Organization
        {
            Name = "Тестовое планирование",
            PrimaryScenarioCode = scenario,
            OwnerUser = owner,
            Members = [new OrganizationMember { User = owner, RoleCode = OrganizationRoleCodes.Owner }]
        };
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();

        return organization;
    }

    internal ServiceProvider CreateServices(OpenCRMContext context, IOrganizationSubscriptionService? permissions = null)
    {
        permissions ??= CreateEnabledPermissions().Object;

        return new ServiceCollection()
            .AddSingleton(context)
            .AddSingleton<IBrandRepository>(new BrandRepository(context))
            .AddSingleton<IModelRepository>(new ModelRepository(context))
            .AddSingleton(permissions)
            .AddOpenCRMCatalog()
            .AddOpenCRMPlanning()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    public async Task InitializeAsync()
    {
        await this._postgres.StartAsync();
        await using var context = this.CreateContext();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => this._postgres.DisposeAsync().AsTask();

    internal static Mock<IOrganizationSubscriptionService> CreateEnabledPermissions()
    {
        var permissions = new Mock<IOrganizationSubscriptionService>();
        permissions.Setup(value => value.HasFeatureAsync(It.IsAny<string>(), It.IsAny<Guid>(), FeatureCodes.SchedulerModule))
            .ReturnsAsync(true);
        permissions.Setup(value => value.EnsureWorkspaceCanReadAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        permissions.Setup(value => value.EnsureWorkspaceCanWriteAsync(It.IsAny<string>(), It.IsAny<Guid>()))
            .Returns(Task.CompletedTask);
        permissions.Setup(value => value.EnsureWorkspaceCanWriteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        return permissions;
    }
}
