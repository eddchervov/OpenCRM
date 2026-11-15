using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain;
using OpenCRM.Modules.Communications.Contracts;
using OpenCRM.Modules.Communications.Contracts.Dtos;

namespace OpenCRM.Modules.Communications.Tests.Integration;

[Collection(ConversationDatabaseCollection.Name)]
public sealed class ChannelRevocationIntegrationTests(ConversationDatabaseFixture fixture)
{
    [Theory]
    [InlineData("telegram", true)]
    [InlineData("telegram", false)]
    [InlineData("max", true)]
    [InlineData("max", false)]
    public async Task ExplicitDisconnectRevokesRoutesAndPreventsInboundFromReopeningThem(string provider, bool hasConversation)
    {
        await using var context = fixture.CreateContext();
        var (organization, owner, _) = await fixture.CreateOrganizationAsync(context);
        var botId = hasConversation ? 123451L : 123452L;
        var botIdentity = botId.ToString(CultureInfo.InvariantCulture);
        var client = new OrganizationClient { OrganizationId = organization.Id, Name = "Отключённый клиент" };
        context.OrganizationClients.Add(client);
        if (provider == "telegram")
        {
            context.OrganizationTelegramBots.Add(ConversationDatabaseFixture.AddBot(organization, botId));
        }
        else
        {
            context.OrganizationMaxBots.Add(new OrganizationMaxBot
            {
                OrganizationId = organization.Id,
                ImmutableProviderBotId = botIdentity,
                Username = $"max_revoke_{botIdentity}_bot",
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
            ImmutableProviderIdentity = botIdentity,
            IsVerified = true,
            VerifiedAt = DateTime.UtcNow
        });
        Assert.True(await channels.BindClientAsync(
            organization.Id, connectionId, "98765", "98765", client.Id, DateTime.UtcNow));
        var conversation = await context.OrganizationConversations.SingleAsync(item => item.OrganizationId == organization.Id);
        if (!hasConversation)
        {
            context.OrganizationConversations.Remove(conversation);
            await context.SaveChangesAsync();
        }

        OrganizationConversationMessage? queued = null;
        if (hasConversation)
        {
            var sent = await conversations.SendAsync(owner.Id, organization.Id, conversation.Id,
                new SendConversationMessageDto { RequestId = Guid.NewGuid(), Text = "Будет отменено при отключении" });
            queued = await context.OrganizationConversationMessages.SingleAsync(item => item.Id == sent.Id);
            queued.Status = "sending";
            await context.SaveChangesAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => channels.RevokeClientAsync(
                organization.Id, client.Id, provider, DateTime.UtcNow));
            queued.Status = "queued";
            await context.SaveChangesAsync();
        }

        await channels.RevokeClientAsync(organization.Id, client.Id, provider, DateTime.UtcNow);
        Assert.False(await context.OrganizationClientChannelLinks.AnyAsync(item => item.ClientId == client.Id && item.IsActive));
        var routes = await conversations.GetClientRoutesAsync(owner.Id, organization.Id, client.Id);
        Assert.Empty(routes!.ActiveRoutes);
        if (hasConversation)
        {
            Assert.Equal("cancelled", queued!.Status);
            Assert.Equal("client-channel-disconnected", queued.ErrorCode);
            Assert.Single(routes.HistoryRoutes);
            await Assert.ThrowsAsync<InvalidOperationException>(() => conversations.SendAsync(
                owner.Id, organization.Id, conversation.Id,
                new SendConversationMessageDto { RequestId = Guid.NewGuid(), Text = "Не должно отправиться" }));
        }

        Assert.False(await channels.IngestMessageAsync(new ChannelMessageReceivedDto
        {
            OrganizationId = organization.Id,
            ConnectionId = connectionId,
            ProviderCode = provider,
            ExternalConversationId = "98765",
            ExternalUserId = "98765",
            ProviderMessageId = "after-disconnect",
            Text = "Не должно восстановить подключение",
            OccurredAt = DateTime.UtcNow
        }));
    }
}
