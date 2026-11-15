using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Migrations;
using Testcontainers.PostgreSql;

namespace OpenCRM.Modules.Maintenance.Tests.Integration;

public sealed class MaintenanceDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:15-alpine").Build();

    public OpenCRMContext CreateContext(params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<OpenCRMContext>()
            .UseNpgsql(this._postgres.GetConnectionString(), db =>
            {
                db.MigrationsAssembly(typeof(AssemblyMarker).Assembly.GetName().Name);
                db.EnableRetryOnFailure();
            })
            .AddInterceptors(interceptors)
            .Options;

        return new OpenCRMContext(options);
    }

    public async Task InitializeAsync()
    {
        await this._postgres.StartAsync();
        await using var context = this.CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await this._postgres.DisposeAsync();
    }
}
