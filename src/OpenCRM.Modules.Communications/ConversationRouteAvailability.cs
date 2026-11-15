using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;

namespace OpenCRM.Modules.Communications;

internal sealed class ConversationRouteAvailability(OpenCRMContext context)
{
    private const string Telegram = "telegram";

    public async Task<bool> IsCurrentAsync(OrganizationConversation conversation, CancellationToken cancellationToken)
    {
        if (conversation.ConnectionId.HasValue)
        {
            var route = await context.OrganizationChannelConnections.AsNoTracking()
                .Where(item => item.Id == conversation.ConnectionId.Value
                    && item.OrganizationId == conversation.OrganizationId)
                .Select(item => new { item.ProviderCode, item.ImmutableProviderIdentity, item.IsCurrent })
                .SingleOrDefaultAsync(cancellationToken);
            if (route is null || !route.IsCurrent)
            {
                return false;
            }

            if (route.ProviderCode == Telegram
                && long.TryParse(route.ImmutableProviderIdentity, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var botId))
            {
                return await context.OrganizationTelegramBots.AsNoTracking()
                    .AnyAsync(item => item.OrganizationId == conversation.OrganizationId
                        && item.TelegramBotId == botId && item.IsEnabled, cancellationToken);
            }

            if (route.ProviderCode == "max")
            {
                return await context.OrganizationMaxBots.AsNoTracking()
                    .AnyAsync(item => item.OrganizationId == conversation.OrganizationId
                        && item.ImmutableProviderBotId == route.ImmutableProviderIdentity && item.IsEnabled, cancellationToken);
            }

            return false;
        }

        return conversation.BotId.HasValue
            && await context.OrganizationTelegramBots.AsNoTracking()
                .AnyAsync(item => item.OrganizationId == conversation.OrganizationId
                    && item.TelegramBotId == conversation.BotId.Value && item.IsEnabled, cancellationToken);
    }
}
