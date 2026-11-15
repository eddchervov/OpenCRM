using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Accounts.Users;
using OpenCRM.Services.Interfaces.Users;
using OpenCRM.Services.Interfaces.Users.Dtos;

namespace OpenCRM.Modules.Accounts.Tests.Integration;

public sealed class UsersServiceTests(AccountsDatabaseFixture fixture) : IClassFixture<AccountsDatabaseFixture>
{
    private static UsersService CreateTrackingService(OpenCRMContext context, IUserAccessInfoAccessor? accessor = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["UserTracking:Enabled"] = "true"
        }).Build();

        return new UsersService(context, accessor, configuration);
    }

    [Fact]
    public async Task DefaultModeDoesNotCollectVisitCountersOrClientFingerprint()
    {
        var user = CreateUser();
        await using var context = fixture.CreateContext();
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var accessor = new StubUserAccessInfoAccessor(new UserAccessInfoDto { IpAddress = "192.0.2.1", UserAgent = "Test browser" });
        var service = new UsersService(context, accessor);

        await service.RecordLastDateOfEntry(user.Id);
        await service.RecordLastAccessAsync(user.Id);
        await service.RecordLastLoginDate(user.Id, "password");
        await context.Entry(user).ReloadAsync();

        Assert.Equal(0, user.CountVisits);
        Assert.Equal(0, user.CountLogins);
        Assert.Null(user.LastAccessAt);
        Assert.Null(user.LastAccessIpAddress);
        Assert.Null(user.LastAccessUserAgent);
        Assert.Equal("password", user.LastLoginMethod);
        Assert.InRange(user.LastLoginDate, DateTime.UtcNow.AddSeconds(-10), DateTime.UtcNow.AddSeconds(10));
    }

    [Fact]
    public async Task RecordLastDateOfEntryUpdatesVisitTimestampAndCounter()
    {
        var user = CreateUser();
        await using var context = fixture.CreateContext();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        await CreateTrackingService(context).RecordLastDateOfEntry(user.Id);

        await using var verificationContext = fixture.CreateContext();
        var updated = await verificationContext.Users.AsNoTracking().SingleAsync(item => item.Id == user.Id);
        Assert.Equal(1, updated.CountVisits);
        Assert.InRange(updated.LastDateOfEntry, DateTime.UtcNow.AddSeconds(-10), DateTime.UtcNow.AddSeconds(10));
    }

    [Fact]
    public async Task RecordLastDateOfEntryThrowsWhenUserDoesNotExist()
    {
        await using var context = fixture.CreateContext();

        var error = await Assert.ThrowsAsync<Exception>(
            () => CreateTrackingService(context).RecordLastDateOfEntry($"missing-{Guid.NewGuid():N}"));

        Assert.Contains("missing-", error.Message);
    }

    [Fact]
    public async Task RecordLastLoginDateUpdatesLoginTimestampAndCounter()
    {
        var user = CreateUser();
        await using var context = fixture.CreateContext();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        await CreateTrackingService(context).RecordLastLoginDate(user.Id);

        await using var verificationContext = fixture.CreateContext();
        var updated = await verificationContext.Users.AsNoTracking().SingleAsync(item => item.Id == user.Id);
        Assert.Equal(1, updated.CountLogins);
        Assert.InRange(updated.LastLoginDate, DateTime.UtcNow.AddSeconds(-10), DateTime.UtcNow.AddSeconds(10));
    }

    [Fact]
    public async Task RecordLastLoginDateSavesAuthenticationMethodAndClientMetadata()
    {
        var user = CreateUser();
        await using var context = fixture.CreateContext();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var access = new UserAccessInfoDto
        {
            IpAddress = "192.0.2.1",
            UserAgent = "Accounts integration test",
            Language = "ru-RU,ru;q=0.9",
            ClientBrands = "Chromium",
            Platform = "Windows",
            IsMobile = false,
            AuthenticationScheme = "Identity.Application"
        };
        await CreateTrackingService(context, new StubUserAccessInfoAccessor(access))
            .RecordLastLoginDate(user.Id, "password");

        await using var verificationContext = fixture.CreateContext();
        var updated = await verificationContext.Users.AsNoTracking().SingleAsync(item => item.Id == user.Id);
        Assert.Equal(1, updated.CountLogins);
        Assert.InRange(updated.LastLoginDate, DateTime.UtcNow.AddSeconds(-10), DateTime.UtcNow.AddSeconds(10));
        Assert.Equal("password", updated.LastLoginMethod);
        Assert.InRange(updated.LastAccessAt!.Value, DateTime.UtcNow.AddSeconds(-10), DateTime.UtcNow.AddSeconds(10));
        Assert.Equal(access.IpAddress, updated.LastAccessIpAddress);
        Assert.Equal(access.UserAgent, updated.LastAccessUserAgent);
        Assert.Equal(access.Language, updated.LastAccessLanguage);
        Assert.Equal(access.ClientBrands, updated.LastAccessClientBrands);
        Assert.Equal(access.Platform, updated.LastAccessPlatform);
        Assert.Equal(access.IsMobile, updated.LastAccessIsMobile);
        Assert.Equal(access.AuthenticationScheme, updated.LastAccessAuthenticationScheme);
    }

    [Fact]
    public async Task RecordLastAccessUpdatesLatestClientSnapshotWithoutChangingLoginData()
    {
        var user = CreateUser();
        var lastLoginDate = new DateTime(2026, 10, 1, 12, 30, 0, DateTimeKind.Utc);
        user.CountLogins = 4;
        user.LastLoginDate = lastLoginDate;
        user.LastLoginMethod = "password";
        user.LastAccessAt = lastLoginDate;
        user.LastAccessIpAddress = "192.0.2.1";
        await using var context = fixture.CreateContext();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var access = new UserAccessInfoDto
        {
            IpAddress = "192.0.2.1",
            UserAgent = "Returned session",
            Language = "en-US",
            ClientBrands = "Firefox",
            Platform = "Linux",
            IsMobile = true,
            AuthenticationScheme = "Bearer"
        };
        await CreateTrackingService(context, new StubUserAccessInfoAccessor(access)).RecordLastAccessAsync(user.Id);

        await using var verificationContext = fixture.CreateContext();
        var updated = await verificationContext.Users.AsNoTracking().SingleAsync(item => item.Id == user.Id);
        Assert.Equal(4, updated.CountLogins);
        Assert.Equal(lastLoginDate, updated.LastLoginDate);
        Assert.Equal("password", updated.LastLoginMethod);
        Assert.True(updated.LastAccessAt > lastLoginDate);
        Assert.Equal(access.IpAddress, updated.LastAccessIpAddress);
        Assert.Equal(access.UserAgent, updated.LastAccessUserAgent);
        Assert.Equal(access.Language, updated.LastAccessLanguage);
        Assert.Equal(access.ClientBrands, updated.LastAccessClientBrands);
        Assert.Equal(access.Platform, updated.LastAccessPlatform);
        Assert.Equal(access.IsMobile, updated.LastAccessIsMobile);
        Assert.Equal(access.AuthenticationScheme, updated.LastAccessAuthenticationScheme);
    }

    [Fact]
    public async Task RecordLastAccessClearsFieldsMissingFromLatestRequest()
    {
        var user = CreateUser();
        user.LastAccessAt = DateTime.UtcNow.AddDays(-1);
        user.LastAccessIpAddress = "192.0.2.1";
        user.LastAccessUserAgent = "Previous agent";
        user.LastAccessLanguage = "ru-RU";
        user.LastAccessClientBrands = "Previous brand";
        user.LastAccessPlatform = "Previous platform";
        user.LastAccessIsMobile = true;
        user.LastAccessAuthenticationScheme = "Previous scheme";
        await using var context = fixture.CreateContext();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        await CreateTrackingService(context, new StubUserAccessInfoAccessor(new UserAccessInfoDto
        {
            IpAddress = "192.0.2.1"
        })).RecordLastAccessAsync(user.Id);

        await using var verificationContext = fixture.CreateContext();
        var updated = await verificationContext.Users.AsNoTracking().SingleAsync(item => item.Id == user.Id);
        Assert.Equal("192.0.2.1", updated.LastAccessIpAddress);
        Assert.Null(updated.LastAccessUserAgent);
        Assert.Null(updated.LastAccessLanguage);
        Assert.Null(updated.LastAccessClientBrands);
        Assert.Null(updated.LastAccessPlatform);
        Assert.Null(updated.LastAccessIsMobile);
        Assert.Null(updated.LastAccessAuthenticationScheme);
    }

    [Fact]
    public async Task RecordLastAccessDoesNothingWithoutAccessor()
    {
        var user = CreateUser();
        var previousAccessAt = new DateTime(2026, 10, 1, 12, 30, 0, DateTimeKind.Utc);
        user.LastAccessAt = previousAccessAt;
        user.LastAccessIpAddress = "192.0.2.1";
        await using var context = fixture.CreateContext();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        await CreateTrackingService(context).RecordLastAccessAsync(user.Id);

        await using var verificationContext = fixture.CreateContext();
        var unchanged = await verificationContext.Users.AsNoTracking().SingleAsync(item => item.Id == user.Id);
        Assert.Equal(previousAccessAt, unchanged.LastAccessAt);
        Assert.Equal("192.0.2.1", unchanged.LastAccessIpAddress);
    }

    [Fact]
    public async Task RecordLastAccessDoesNotUpdateDeletedUser()
    {
        var user = CreateUser();
        user.IsDeleted = true;
        var previousAccessAt = new DateTime(2026, 10, 1, 12, 30, 0, DateTimeKind.Utc);
        user.LastAccessAt = previousAccessAt;
        user.LastAccessIpAddress = "192.0.2.1";
        await using var context = fixture.CreateContext();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        await CreateTrackingService(context, new StubUserAccessInfoAccessor(new UserAccessInfoDto
        {
            IpAddress = "192.0.2.1"
        })).RecordLastAccessAsync(user.Id);

        await using var verificationContext = fixture.CreateContext();
        var unchanged = await verificationContext.Users.AsNoTracking().SingleAsync(item => item.Id == user.Id);
        Assert.Equal(previousAccessAt, unchanged.LastAccessAt);
        Assert.Equal("192.0.2.1", unchanged.LastAccessIpAddress);
    }

    [Fact]
    public async Task RecordLastAccessDoesNotSaveUnrelatedTrackedChanges()
    {
        var user = CreateUser();
        var originalEmail = user.Email;
        await using var context = fixture.CreateContext();
        context.Users.Add(user);
        await context.SaveChangesAsync();
        user.Email = $"unsaved-{Guid.NewGuid():N}@example.com";

        await CreateTrackingService(context, new StubUserAccessInfoAccessor(new UserAccessInfoDto
        {
            IpAddress = "192.0.2.1"
        })).RecordLastAccessAsync(user.Id);

        Assert.Equal(EntityState.Modified, context.Entry(user).State);
        await using var verificationContext = fixture.CreateContext();
        var stored = await verificationContext.Users.AsNoTracking().SingleAsync(item => item.Id == user.Id);
        Assert.Equal(originalEmail, stored.Email);
        Assert.Equal("192.0.2.1", stored.LastAccessIpAddress);
    }

    [Fact]
    public async Task RecordLastLoginDateThrowsWhenUserDoesNotExist()
    {
        await using var context = fixture.CreateContext();

        var error = await Assert.ThrowsAsync<Exception>(
            () => CreateTrackingService(context).RecordLastLoginDate($"missing-{Guid.NewGuid():N}"));

        Assert.Contains("missing-", error.Message);
    }

    [Fact]
    public async Task GetPersonalStatsCountsOnlyActivePersonalVehiclesAndMaintenance()
    {
        var user = CreateUser();
        var organization = new Organization { OwnerUserId = user.Id, OwnerUser = user, Name = "Accounts stats test" };
        var personalVehicle = new Vehicle { UserId = user.Id, User = user };
        var organizationVehicle = new Vehicle
        {
            UserId = user.Id,
            User = user,
            OrganizationId = organization.Id,
            Organization = organization
        };
        var deletedVehicle = new Vehicle { UserId = user.Id, User = user, IsDeleted = true };
        await using var context = fixture.CreateContext();
        context.Users.Add(user);
        context.Organizations.Add(organization);
        context.Vehicles.AddRange(personalVehicle, organizationVehicle, deletedVehicle);
        context.Maintenance.AddRange(
            MaintenanceFor(user, personalVehicle),
            MaintenanceFor(user, organizationVehicle),
            MaintenanceFor(user, deletedVehicle, isDeleted: true));
        await context.SaveChangesAsync();

        var stats = await CreateTrackingService(context).GetPersonalStatsAsync(user.Id);

        Assert.Equal(1, stats.VehiclesCount);
        Assert.Equal(1, stats.MaintenanceCount);
    }

    [Fact]
    public async Task HasDeletedOriginalEmailFindsOnlyDeletedAccountAndReturnsFalseForBlankEmails()
    {
        var deleted = CreateUser();
        deleted.IsDeleted = true;
        deleted.OriginalEmail = $"original-{Guid.NewGuid():N}@example.com";
        var active = CreateUser();
        active.OriginalEmail = $"active-original-{Guid.NewGuid():N}@example.com";
        await using var context = fixture.CreateContext();
        context.Users.AddRange(deleted, active);
        await context.SaveChangesAsync();

        var service = CreateTrackingService(context);

        Assert.True(await service.HasDeletedOriginalEmailAsync(deleted.OriginalEmail));
        Assert.False(await service.HasDeletedOriginalEmailAsync(active.OriginalEmail));
        Assert.False(await service.HasDeletedOriginalEmailAsync($"unknown-{Guid.NewGuid():N}@example.com"));
        Assert.False(await service.HasDeletedOriginalEmailAsync(null));
        Assert.False(await service.HasDeletedOriginalEmailAsync(string.Empty));
        Assert.False(await service.HasDeletedOriginalEmailAsync("   "));
    }

    private static ApplicationUser CreateUser()
    {
        var id = Guid.NewGuid().ToString("N");
        var email = $"accounts-users-{id}@example.com";

        return new ApplicationUser
        {
            Id = id,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant()
        };
    }

    private static global::OpenCRM.Domain.Maintenance MaintenanceFor(
        ApplicationUser user,
        Vehicle vehicle,
        bool isDeleted = false) => new()
    {
        UserId = user.Id,
        User = user,
        VehicleId = vehicle.Id,
        Vehicle = vehicle,
        IsDeleted = isDeleted
    };

    private sealed class StubUserAccessInfoAccessor(UserAccessInfoDto? accessInfo) : IUserAccessInfoAccessor
    {
        public UserAccessInfoDto? GetCurrent() => accessInfo;
    }
}
