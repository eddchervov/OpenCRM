using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.WebApp.Infrastructure.UserActivity;

namespace OpenCRM.Infrastructure.Tests.Integration;

public sealed class UserActivityTests(UserActivityFixture fixture) : IClassFixture<UserActivityFixture>
{
    [Fact]
    public async Task BatchUsesAuthenticatedIdentityAndAdminFeedSupportsStableCursorAndFilters()
    {
        var user = await this.CreateUserAsync();
        await using var host = await UserActivityTestHost.CreateAsync(fixture);
        await host.SignInAsync(user.Id);
        var batch = UserActivityTests.CreateBatch();
        batch.Events.Add(new UserActivityInput
        {
            Kind = "click", Action = "save", Page = "/clients/detailing/clients/{id}",
            Scenario = "detailing", Section = "clients", OccurredAt = DateTimeOffset.UtcNow
        });
        var payload = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(batch))!.AsObject();
        payload["userId"] = "forged-user";
        using var accepted = await host.Client.PostAsJsonAsync("/api/v1/user-activity", payload);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        await using var context = fixture.CreateContext();
        Assert.False(await context.Set<UserActivityEvent>().AnyAsync(activity => activity.UserId == user.Id));
        await host.CreateWriter().FlushAsync(default);
        Assert.Equal(2, await context.Set<UserActivityEvent>().CountAsync(activity => activity.UserId == user.Id));
        using var denied = await host.Client.GetAsync("/api/v1/admin/user-activity");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await host.SignInAsync(user.Id, admin: true);
        using var first = await host.Client.GetAsync($"/api/v1/admin/user-activity?userId={user.Id}&pageSize=1");
        first.EnsureSuccessStatusCode();
        using var firstJson = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        var firstItem = Assert.Single(firstJson.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(user.Id, firstItem.GetProperty("userId").GetString());
        var cursor = firstJson.RootElement.GetProperty("nextBeforeId").GetInt64();
        host.Buffer.TryWrite(UserActivityTests.CreateEvent(user.Id));
        await host.CreateWriter().FlushAsync(default);
        using var next = await host.Client.GetAsync($"/api/v1/admin/user-activity?userId={user.Id}&pageSize=1&beforeId={cursor}");
        next.EnsureSuccessStatusCode();
        using var nextJson = JsonDocument.Parse(await next.Content.ReadAsStringAsync());
        var nextItem = Assert.Single(nextJson.RootElement.GetProperty("items").EnumerateArray());
        Assert.True(nextItem.GetProperty("id").GetInt64() < cursor);
        Assert.Equal(JsonValueKind.Null, nextJson.RootElement.GetProperty("nextBeforeId").ValueKind);
        using var filtered = await host.Client.GetAsync($"/api/v1/admin/user-activity?userId={user.Id}&kind=click&scenario=detailing&search=save");
        using var filteredJson = JsonDocument.Parse(await filtered.Content.ReadAsStringAsync());
        Assert.Single(filteredJson.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("no-store", filtered.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task ReceiverRequiresAuthenticationAntiforgeryAndOrganizationMembership()
    {
        var user = await this.CreateUserAsync();
        await using var host = await UserActivityTestHost.CreateAsync(fixture);
        using var anonymous = await host.Client.PostAsJsonAsync("/api/v1/user-activity", UserActivityTests.CreateBatch());
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        await host.SignInAsync(user.Id);
        host.Client.DefaultRequestHeaders.Remove("RequestVerificationToken");
        using var noToken = await host.Client.PostAsJsonAsync("/api/v1/user-activity", UserActivityTests.CreateBatch());
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        await host.SignInAsync(user.Id);
        var foreignBatch = UserActivityTests.CreateBatch();
        foreignBatch.OrganizationId = Guid.NewGuid();
        using var forbidden = await host.Client.PostAsJsonAsync("/api/v1/user-activity", foreignBatch);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        await using var context = fixture.CreateContext();
        var organization = new Organization { OwnerUserId = user.Id, Name = "Студия активности" };
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();
        foreignBatch.OrganizationId = organization.Id;
        using var owned = await host.Client.PostAsJsonAsync("/api/v1/user-activity", foreignBatch);
        Assert.Equal(HttpStatusCode.Accepted, owned.StatusCode);
        foreach (var consent in new[] { "necessary", "" })
        {
            await host.SignInAsync(user.Id, consent: consent);
            using var accepted = await host.Client.PostAsJsonAsync("/api/v1/user-activity", UserActivityTests.CreateBatch());
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        }

        await host.SignInAsync(user.Id, impersonate: true);
        using var impersonation = await host.Client.PostAsJsonAsync("/api/v1/user-activity", UserActivityTests.CreateBatch());
        Assert.Equal(HttpStatusCode.Gone, impersonation.StatusCode);
    }

    [Fact]
    public async Task ReceiverBoundsBatchFieldsTimeAndRequestRate()
    {
        var user = await this.CreateUserAsync();
        await using var host = await UserActivityTestHost.CreateAsync(fixture);
        await host.SignInAsync(user.Id);
        var oversized = UserActivityTests.CreateBatch();
        oversized.Events = Enumerable.Range(0, 51).Select(_ => UserActivityTests.CreateBatch().Events[0]).ToList();
        var invalidUrl = UserActivityTests.CreateBatch();
        invalidUrl.Events[0].Page = "/clients?email=contact@example.invalid";
        var future = UserActivityTests.CreateBatch();
        future.Events[0].OccurredAt = DateTimeOffset.UtcNow.AddHours(1);
        var emptySession = UserActivityTests.CreateBatch();
        emptySession.SessionId = Guid.Empty;
        var nullEvent = UserActivityTests.CreateBatch();
        nullEvent.Events[0] = null!;
        foreach (var invalid in new[] { oversized, invalidUrl, future, emptySession, nullEvent })
        {
            using var response = await host.Client.PostAsJsonAsync("/api/v1/user-activity", invalid);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        var statuses = new List<HttpStatusCode>();
        for (var index = 0; index < 13; index++)
        {
            using var response = await host.Client.PostAsJsonAsync("/api/v1/user-activity", UserActivityTests.CreateBatch());
            statuses.Add(response.StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
        Assert.True(statuses.Count(status => status == HttpStatusCode.Accepted) <= 12);
    }

    [Fact]
    public async Task DisablingStopsAndDiscardsBufferedCollectionButPreservesExistingHistory()
    {
        var user = await this.CreateUserAsync();
        await using var host = await UserActivityTestHost.CreateAsync(fixture);
        await host.SignInAsync(user.Id, admin: true);
        host.Buffer.TryWrite(UserActivityTests.CreateEvent(user.Id));
        await host.CreateWriter().FlushAsync(default);
        host.Buffer.TryWrite(UserActivityTests.CreateEvent(user.Id));
        host.Settings.Enabled = false;
        Assert.False(host.Buffer.TryWrite(UserActivityTests.CreateEvent(user.Id)));
        await host.CreateWriter().FlushAsync(default);
        using var disabled = await host.Client.PostAsJsonAsync("/api/v1/user-activity", UserActivityTests.CreateBatch());
        Assert.Equal(HttpStatusCode.Gone, disabled.StatusCode);
        using var feed = await host.Client.GetAsync($"/api/v1/admin/user-activity?userId={user.Id}");
        feed.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await feed.Content.ReadAsStringAsync());
        Assert.False(json.RootElement.GetProperty("enabled").GetBoolean());
        Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task WriterBoundsMemoryCleansRetentionAndIgnoresMissingReferences()
    {
        var user = await this.CreateUserAsync();
        await using var host = await UserActivityTestHost.CreateAsync(fixture);
        for (var index = 0; index < 2000; index++)
        {
            Assert.True(host.Buffer.TryWrite(UserActivityTests.CreateEvent(user.Id)));
        }

        Assert.False(host.Buffer.TryWrite(UserActivityTests.CreateEvent(user.Id)));
        var writer = host.CreateWriter();
        await writer.FlushAsync(default);
        await using var context = fixture.CreateContext();
        Assert.Equal(200, await context.Set<UserActivityEvent>().CountAsync(activity => activity.UserId == user.Id));
        host.Settings.Enabled = false;
        for (var index = 0; index < 9; index++)
        {
            await writer.FlushAsync(default);
        }

        host.Settings.Enabled = true;
        var missingOrganization = UserActivityTests.CreateEvent(user.Id);
        missingOrganization.OrganizationId = Guid.NewGuid();
        host.Buffer.TryWrite(missingOrganization);
        host.Buffer.TryWrite(UserActivityTests.CreateEvent(Guid.NewGuid().ToString()));
        await writer.FlushAsync(default);
        Assert.Null(await context.Set<UserActivityEvent>().Where(activity => activity.Id == missingOrganization.Id)
            .Select(activity => activity.OrganizationId).SingleAsync());

        var expired = UserActivityTests.CreateEvent(user.Id);
        expired.ReceivedAt = DateTime.UtcNow.AddDays(-91);
        context.Add(expired);
        await context.SaveChangesAsync();
        Assert.Equal(1, await writer.CleanupAsync(default));
        Assert.False(await context.Set<UserActivityEvent>().AnyAsync(activity => activity.Id == expired.Id));
    }

    [Theory]
    [InlineData("all")]
    [InlineData("necessary")]
    [InlineData("")]
    public async Task OperationRecordsServerOutcomeWithoutAnalyticsConsentAndSuppressesImpersonation(string consent)
    {
        var user = await this.CreateUserAsync();
        await using var host = await UserActivityTestHost.CreateAsync(fixture);
        await host.SignInAsync(user.Id, consent: consent);
        var organizationId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        using var operation = await host.Client.PostAsJsonAsync(
            $"/api/v1/test-operation/{organizationId}/{targetId}?password=secret", new { name = "Частное имя" });
        Assert.Equal(HttpStatusCode.Conflict, operation.StatusCode);
        await host.CreateWriter().FlushAsync(default);
        await using var context = fixture.CreateContext();
        var activity = await context.Set<UserActivityEvent>().SingleAsync(activity => activity.UserId == user.Id);
        Assert.Equal("operation", activity.Kind);
        Assert.Equal("POST:error:409", activity.Action);
        Assert.Equal("/api/v1/test-operation/{organizationId:guid}/{id:guid}", activity.Page);
        Assert.Equal(targetId.ToString(), activity.TargetId);
        Assert.Null(activity.OrganizationId);
        await host.SignInAsync(user.Id, consent: consent, impersonate: true);
        using var suppressed = await host.Client.PostAsync($"/api/v1/test-operation/{organizationId}/{targetId}", null);
        await host.CreateWriter().FlushAsync(default);
        Assert.Equal(1, await context.Set<UserActivityEvent>().CountAsync(activity => activity.UserId == user.Id));
    }

    [Fact]
    public async Task MigrationRoundTripMatchesModelAndRemovesOnlyActivityTable()
    {
        await using var context = fixture.CreateContext();
        Assert.False(context.Database.HasPendingModelChanges());
        var user = await this.CreateUserAsync();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20261021161413_AddUserLastAccessInfo");
        Assert.True(await context.Users.AnyAsync(candidate => candidate.Id == user.Id));
        Assert.False(await context.Database.SqlQuery<bool>($"SELECT to_regclass('admin.\"UserActivityEvents\"') IS NOT NULL AS \"Value\"").SingleAsync());
        await migrator.MigrateAsync();
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.True(await context.Database.SqlQuery<bool>($"SELECT to_regclass('admin.\"UserActivityEvents\"') IS NOT NULL AS \"Value\"").SingleAsync());
    }

    private async Task<ApplicationUser> CreateUserAsync()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser { Email = $"activity-{Guid.NewGuid():N}@example.test", EmailConfirmed = true };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        return user;
    }

    private static UserActivityBatch CreateBatch() => new()
    {
        SessionId = Guid.NewGuid(),
        Events = [new UserActivityInput
        {
            Kind = "page", Action = "view", Page = "/clients/detailing/scheduler", Scenario = "detailing",
            Section = "scheduler", OccurredAt = DateTimeOffset.UtcNow
        }]
    };

    private static UserActivityEvent CreateEvent(string userId) => new()
    {
        UserId = userId, Kind = "page", Action = "view", Page = "/personal", Scenario = "personal", Section = "personal",
        SessionId = Guid.NewGuid(), OccurredAt = DateTime.UtcNow, ReceivedAt = DateTime.UtcNow
    };
}
