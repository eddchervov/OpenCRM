using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Migrations;
using Testcontainers.PostgreSql;

namespace OpenCRM.Modules.Accounts.Tests.Integration;

public sealed class UserLastAccessMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261021030048_AddClientIdentityFields";
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:15-alpine").Build();

    [Fact]
    public async Task RollingBackAndReapplyingMigrationPreservesExistingUserLoginData()
    {
        var userId = Guid.NewGuid().ToString("N");
        var lastLoginDate = new DateTime(2026, 10, 1, 12, 30, 0, DateTimeKind.Utc);
        await using (var context = this.CreateContext())
        {
            context.Users.Add(new ApplicationUser
            {
                Id = userId,
                UserName = $"migration-{userId}@example.test",
                NormalizedUserName = $"MIGRATION-{userId}@EXAMPLE.TEST",
                Email = $"migration-{userId}@example.test",
                NormalizedEmail = $"MIGRATION-{userId}@EXAMPLE.TEST",
                LastDateOfEntry = lastLoginDate.AddDays(-1),
                LastLoginDate = lastLoginDate,
                CountVisits = 6,
                CountLogins = 3
            });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);
            await migrator.MigrateAsync();
        }

        await using var verificationContext = this.CreateContext();
        var user = await verificationContext.Users.AsNoTracking().SingleAsync(item => item.Id == userId);

        Assert.Equal(lastLoginDate.AddDays(-1), user.LastDateOfEntry);
        Assert.Equal(lastLoginDate, user.LastLoginDate);
        Assert.Equal(6, user.CountVisits);
        Assert.Equal(3, user.CountLogins);
        Assert.Null(user.LastLoginMethod);
        Assert.Null(user.LastAccessAt);
        Assert.Null(user.LastAccessIpAddress);
        Assert.Null(user.LastAccessUserAgent);
        Assert.Null(user.LastAccessLanguage);
        Assert.Null(user.LastAccessClientBrands);
        Assert.Null(user.LastAccessPlatform);
        Assert.Null(user.LastAccessIsMobile);
        Assert.Null(user.LastAccessAuthenticationScheme);
        Assert.False(verificationContext.Database.HasPendingModelChanges());
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

    private OpenCRMContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<OpenCRMContext>()
            .UseNpgsql(this._postgres.GetConnectionString(), database =>
            {
                database.MigrationsAssembly(typeof(AssemblyMarker).Assembly.GetName().Name);
                database.EnableRetryOnFailure();
            })
            .Options;

        return new OpenCRMContext(options);
    }
}
