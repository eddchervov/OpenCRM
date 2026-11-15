using Microsoft.EntityFrameworkCore;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Migrations;
using Testcontainers.PostgreSql;

namespace OpenCRM.Infrastructure.Tests.Integration;

public sealed class UserActivityFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:15-alpine").Build();

    public OpenCRMContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<OpenCRMContext>().UseNpgsql(
            this._postgres.GetConnectionString(), database => database.MigrationsAssembly(typeof(AssemblyMarker).Assembly.GetName().Name));

        return new OpenCRMContext(options.Options);
    }

    public async Task InitializeAsync()
    {
        await this._postgres.StartAsync();
        await using var context = this.CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await this._postgres.DisposeAsync();
}
