using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain;
using OpenCRM.Modules.Communications.Contracts;
using OpenCRM.Modules.Communications.Contracts.Dtos;
using Npgsql;

namespace OpenCRM.Modules.Communications.Tests.Integration;

[Collection(ConversationDatabaseCollection.Name)]
public sealed class ClientLinkIntegrationTests(ConversationDatabaseFixture fixture)
{
    [Theory]
    [InlineData("telegram")]
    [InlineData("max")]
    public async Task MovingOrUnlinkingClientClearsOldNotificationAddressAndCancelsPendingMessages(string provider)
    {
        var failure = new LinkInsertFailure();
        await using var context = fixture.CreateContext(failure, enableRetries: true);
        var (organization, owner, _) = await fixture.CreateOrganizationAsync(context);
        var previousClient = new OrganizationClient
        {
            OrganizationId = organization.Id,
            Name = "Прежний клиент",
            TelegramChatId = provider == "telegram" ? 567L : null,
            TelegramConnectedAt = provider == "telegram" ? DateTime.UtcNow.AddMinutes(-1) : null,
            TelegramNotificationsEnabled = provider == "telegram",
            MaxUserId = provider == "max" ? 567L : null,
            MaxConnectedAt = provider == "max" ? DateTime.UtcNow.AddMinutes(-1) : null,
            MaxNotificationsEnabled = provider == "max"
        };
        var nextClient = new OrganizationClient { OrganizationId = organization.Id, Name = "Верный клиент" };
        context.OrganizationClients.AddRange(previousClient, nextClient);
        if (provider == "telegram")
        {
            context.OrganizationTelegramBots.Add(ConversationDatabaseFixture.AddBot(organization, 16001));
        }
        else
        {
            context.OrganizationMaxBots.Add(new OrganizationMaxBot
            {
                OrganizationId = organization.Id,
                ImmutableProviderBotId = "16001",
                Username ="max_manual_link_bot",
                ProtectedToken = "protected-token",
                ProtectedWebhookSecret = "protected-secret",
                IsEnabled = true
            });
        }

        await context.SaveChangesAsync();
        using var services = fixture.CreateServices(context);
        var channels = services.GetRequiredService<IOrganizationChannelService>();
        var conversations = services.GetRequiredService<IOrganizationConversationService>();
        var connectionId = await channels.EnsureConnectionAsync(new EnsureOrganizationChannelConnectionDto
        {
            OrganizationId = organization.Id,
            ProviderCode = provider,
            ImmutableProviderIdentity = "16001",
            IsVerified = true,
            VerifiedAt = DateTime.UtcNow
        });
        var externalConversationId = provider == "telegram" ? "567" : "777";
        Assert.True(await channels.BindClientAsync(
            organization.Id, connectionId, externalConversationId, "567", previousClient.Id, DateTime.UtcNow));
        var conversation = await context.OrganizationConversations.SingleAsync(item =>
            item.ConnectionId == connectionId && item.ExternalConversationId == externalConversationId);
        var queued = await conversations.SendAsync(owner.Id, organization.Id, conversation.Id,
            new SendConversationMessageDto { RequestId = Guid.NewGuid(), Text = "Сообщение прежнему клиенту" });
        var message = await context.OrganizationConversationMessages.SingleAsync(item => item.Id == queued.Id);
        message.Status = "sending";
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => conversations.LinkClientAsync(
            owner.Id, organization.Id, conversation.Id, nextClient.Id));
        message.Status = "queued";
        await context.SaveChangesAsync();

        failure.IsArmed = true;
        Assert.True(await conversations.LinkClientAsync(owner.Id, organization.Id, conversation.Id, nextClient.Id));
        Assert.Equal(1, failure.FailureCount);
        previousClient = await context.OrganizationClients.SingleAsync(item => item.Id == previousClient.Id);
        nextClient = await context.OrganizationClients.SingleAsync(item => item.Id == nextClient.Id);
        message = await context.OrganizationConversationMessages.SingleAsync(item => item.Id == queued.Id);
        Assert.Null(previousClient.TelegramChatId);
        Assert.Null(previousClient.MaxUserId);
        Assert.False(previousClient.TelegramNotificationsEnabled);
        Assert.False(previousClient.MaxNotificationsEnabled);
        Assert.False(nextClient.TelegramNotificationsEnabled);
        Assert.False(nextClient.MaxNotificationsEnabled);
        Assert.Equal("cancelled", message.Status);
        Assert.Equal("client-link-changed", message.ErrorCode);
        var activeLink = await context.OrganizationClientChannelLinks.SingleAsync(item =>
            item.ConnectionId == connectionId && item.IsActive);
        Assert.Equal(nextClient.Id, activeLink.ClientId);
        Assert.Equal("567", activeLink.ExternalUserId);
        Assert.True(await context.OrganizationClientChannelLinks.AnyAsync(item =>
            item.ClientId == previousClient.Id && !item.IsActive && item.RevokedAt.HasValue));

        nextClient.TelegramChatId = provider == "telegram" ? 567L : null;
        nextClient.TelegramNotificationsEnabled = provider == "telegram";
        nextClient.MaxUserId = provider == "max" ? 567L : null;
        nextClient.MaxNotificationsEnabled = provider == "max";
        await context.SaveChangesAsync();
        Assert.True(await conversations.LinkClientAsync(owner.Id, organization.Id, conversation.Id, null));
        Assert.Null(nextClient.TelegramChatId);
        Assert.Null(nextClient.MaxUserId);
        Assert.False(nextClient.TelegramNotificationsEnabled);
        Assert.False(nextClient.MaxNotificationsEnabled);
        Assert.False(await context.OrganizationClientChannelLinks.AnyAsync(item =>
            item.ConnectionId == connectionId && item.IsActive));
    }

    private sealed class LinkInsertFailure : DbCommandInterceptor
    {
        public bool IsArmed { get; set; }

        public int FailureCount { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (this.IsArmed && command.CommandText.Contains("INSERT INTO \"OrganizationClientChannelLinks\"", StringComparison.Ordinal))
            {
                this.IsArmed = false;
                this.FailureCount++;
                throw new NpgsqlException("Тестовый сбой после отзыва прежней связи.", new TimeoutException());
            }

            return ValueTask.FromResult(result);
        }
    }
}
