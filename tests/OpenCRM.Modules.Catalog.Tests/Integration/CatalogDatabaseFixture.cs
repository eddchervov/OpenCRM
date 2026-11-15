using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Data.Repositories;
using OpenCRM.Domain.Interfaces.Brands;
using OpenCRM.Domain.Interfaces.Models;
using OpenCRM.Infrastructure.Migrations;
using OpenCRM.Modules.Catalog.Contracts;
using OpenCRM.Services.Interfaces.Organizations;
using Testcontainers.PostgreSql;

namespace OpenCRM.Modules.Catalog.Tests.Integration;

[CollectionDefinition(Name)]
public sealed class CatalogDatabaseCollection : ICollectionFixture<CatalogDatabaseFixture>
{
    public const string Name = "Catalog PostgreSQL database";
}

public sealed class CatalogDatabaseFixture : IAsyncLifetime
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
        var owner = new ApplicationUser { Email = $"catalog-{Guid.NewGuid():N}@example.test" };
        var organization = new Organization
        {
            Name = "Тестовый каталог",
            PrimaryScenarioCode = scenario,
            OwnerUser = owner,
            Members = [new OrganizationMember { User = owner, RoleCode = OrganizationRoleCodes.Owner }]
        };
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();

        return organization;
    }

    internal ServiceProvider CreateServices(OpenCRMContext context, IOrganizationSubscriptionService? permissions = null) =>
        new ServiceCollection().AddSingleton(context)
            .AddSingleton<IBrandRepository>(new BrandRepository(context))
            .AddSingleton<IModelRepository>(new ModelRepository(context))
            .AddSingleton(permissions ?? Mock.Of<IOrganizationSubscriptionService>())
            .AddOpenCRMCatalog().BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

    public async Task InitializeAsync()
    {
        await this._postgres.StartAsync();
        await using var context = this.CreateContext();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => this._postgres.DisposeAsync().AsTask();
}
