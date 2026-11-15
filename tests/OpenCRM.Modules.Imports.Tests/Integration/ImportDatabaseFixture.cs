using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Migrations;
using Testcontainers.PostgreSql;

namespace OpenCRM.Modules.Imports.Tests.Integration;

public sealed class ImportDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:15-alpine").Build();

    public OpenCRMContext CreateContext(params IInterceptor[] interceptors) => new(new DbContextOptionsBuilder<OpenCRMContext>()
        .UseNpgsql(this._postgres.GetConnectionString(), options =>
        {
            options.MigrationsAssembly(typeof(AssemblyMarker).Assembly.GetName().Name);
            options.EnableRetryOnFailure();
        })
        .AddInterceptors(interceptors)
        .Options);

    public async Task InitializeAsync()
    {
        await this._postgres.StartAsync();
        await using var context = this.CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await this._postgres.DisposeAsync();
}
