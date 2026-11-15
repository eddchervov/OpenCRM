using OpenCRM.Services.Interfaces.Emails.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Notifications.Emails;
using OpenCRM.Services.Interfaces.Emails;
using OpenCRM.Testing;

namespace OpenCRM.Modules.Notifications.Tests.Integration;

[Collection(nameof(NotificationDatabaseCollection))]
public sealed class EmailUnsubscribeServiceTests(NotificationFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private const string Secret = "integration-test-unsubscribe-secret";

    [Theory]
    [InlineData("", "person@example.com", "token")]
    [InlineData("user-1", " ", "token")]
    [InlineData("user-1", "person@example.com", "invalid-token")]
    public async Task UnsubscribeAsync_InvalidRequest_ReturnsInvalidLink(
        string userId,
        string email,
        string token)
    {
        await using var context = fixture.CreateContext();
        var service = CreateService(context, Secret);

        var result = await service.UnsubscribeAsync(userId, email, token);

        Assert.Equal(EmailUnsubscribeResult.InvalidLink, result);
    }

    [Fact]
    public async Task UnsubscribeAsync_MissingSecret_ReturnsInvalidLink()
    {
        await using var context = fixture.CreateContext();
        var service = CreateService(context, null);
        var token = EmailUnsubscribeToken.Create("user-1", "person@example.com", Secret);

        var result = await service.UnsubscribeAsync("user-1", "person@example.com", token);

        Assert.Equal(EmailUnsubscribeResult.InvalidLink, result);
    }

    [Fact]
    public async Task UnsubscribeAsync_ValidLinkForUnknownUser_ReturnsSubscriptionNotFound()
    {
        await using var context = fixture.CreateContext();
        var service = CreateService(context, Secret);
        var userId = $"unsubscribe-missing-{Guid.NewGuid():N}";
        var email = $"{userId}@example.com";
        var token = EmailUnsubscribeToken.Create(userId, email, Secret);

        var result = await service.UnsubscribeAsync(userId, email, token);

        Assert.Equal(EmailUnsubscribeResult.SubscriptionNotFound, result);
    }

    [Fact]
    public async Task UnsubscribeAsync_AcceptedConsent_ClearsConsentAndTimestamp()
    {
        var userId = $"unsubscribe-accepted-{Guid.NewGuid():N}";
        var email = $"{userId}@example.com";
        var acceptedAt = DateTime.UtcNow;
        await this.SaveUserAsync(userId, email, true, acceptedAt);

        await using var context = fixture.CreateContext();
        var service = CreateService(context, Secret);
        var token = EmailUnsubscribeToken.Create(userId, email, Secret);

        var result = await service.UnsubscribeAsync(userId, $"  {email.ToUpperInvariant()}  ", token);

        Assert.Equal(EmailUnsubscribeResult.Unsubscribed, result);
        await using var verification = fixture.CreateContext();
        var user = await verification.Users.SingleAsync(candidate => candidate.Id == userId);
        Assert.False(user.AdvertisingConsentAccepted);
        Assert.Null(user.AdvertisingConsentAcceptedAt);
    }

    [Fact]
    public async Task UnsubscribeAsync_ConsentAlreadyDeclined_ReturnsUnsubscribed()
    {
        var userId = $"unsubscribe-declined-{Guid.NewGuid():N}";
        var email = $"{userId}@example.com";
        await this.SaveUserAsync(userId, email, false, null);

        await using var context = fixture.CreateContext();
        var service = CreateService(context, Secret);
        var token = EmailUnsubscribeToken.Create(userId, email, Secret);

        var result = await service.UnsubscribeAsync(userId, email, token);

        Assert.Equal(EmailUnsubscribeResult.Unsubscribed, result);
        await using var verification = fixture.CreateContext();
        var user = await verification.Users.SingleAsync(candidate => candidate.Id == userId);
        Assert.False(user.AdvertisingConsentAccepted);
        Assert.Null(user.AdvertisingConsentAcceptedAt);
    }

    [Fact]
    public async Task UnsubscribeAsync_CanceledQuery_ThrowsOperationCanceledException()
    {
        var userId = $"unsubscribe-canceled-{Guid.NewGuid():N}";
        var email = $"{userId}@example.com";
        await this.SaveUserAsync(userId, email, true, DateTime.UtcNow);

        await using var context = fixture.CreateContext();
        var service = CreateService(context, Secret);
        var token = EmailUnsubscribeToken.Create(userId, email, Secret);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.UnsubscribeAsync(userId, email, token, cancellation.Token));
    }

    private async Task SaveUserAsync(
        string userId,
        string email,
        bool advertisingConsentAccepted,
        DateTime? advertisingConsentAcceptedAt)
    {
        await using var context = fixture.CreateContext();
        context.Users.Add(new ApplicationUser
        {
            Id = userId,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            AdvertisingConsentAccepted = advertisingConsentAccepted,
            AdvertisingConsentAcceptedAt = advertisingConsentAcceptedAt
        });
        await context.SaveChangesAsync();
    }

    private static EmailUnsubscribeService CreateService(OpenCRMContext context, string? secret)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EmailPreferences:UnsubscribeTokenSecret"] = secret
            })
            .Build();

        return new EmailUnsubscribeService(context, configuration);
    }
}
