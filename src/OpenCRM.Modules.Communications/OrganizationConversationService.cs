using System.Data;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Communications.Contracts;
using OpenCRM.Modules.Communications.Contracts.Dtos;

namespace OpenCRM.Modules.Communications;

internal sealed class OrganizationConversationService(
    OpenCRMContext context,
    ConversationAccess access,
    OrganizationChannelService channelService,
    OrganizationConversationDeliveryService deliveryService,
    ConversationRouteAvailability routeAvailability) : IOrganizationConversationService, IConversationBotIdentityRegistry
{
    private const string Incoming = "incoming";
    private const string Outgoing = "outgoing";
    private const string Queued = "queued";
    private const string Sending = "sending";
    private const string Accepted = "accepted";
    private const string Failed = "failed";
    private const string Uncertain = "uncertain";
    private const string Cancelled = "cancelled";
    private const string Telegram = "telegram";
    private const string Max = "max";

    public async Task<ConversationInboxPageDto> GetInboxAsync(
        string userId,
        Guid organizationId,
        ConversationInboxQueryDto query,
        CancellationToken cancellationToken = default)
    {
        await access.EnsureViewAsync(userId, organizationId);
        await ReconcileBotStateAsync(organizationId, cancellationToken);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var search = query.Search?.Trim();
        var canViewClient = await access.HasPermissionAsync(userId, organizationId, "clients.view");
        if (query.ClientId.HasValue)
        {
            await access.EnsurePermissionAsync(userId, organizationId, "clients.view");
        }

        var source = context.OrganizationConversations.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && item.LastMessageAt != null);
        if (query.ClientId.HasValue)
        {
            source = source.Where(item => item.OrganizationClientId == query.ClientId.Value);
        }
        if (!string.IsNullOrEmpty(search))
        {
            source = source.Where(item =>
                (item.TelegramDisplayName != null && EF.Functions.ILike(item.TelegramDisplayName, $"%{search}%"))
                || (item.TelegramUsername != null && EF.Functions.ILike(item.TelegramUsername, $"%{search}%"))
                || (item.LastMessageText != null && EF.Functions.ILike(item.LastMessageText, $"%{search}%"))
                || (canViewClient && item.OrganizationClient != null && EF.Functions.ILike(item.OrganizationClient.Name, $"%{search}%")));
        }

        if (query.OnlyUnread)
        {
            source = source.Where(item => context.OrganizationConversationMessages.Any(message =>
                message.ConversationId == item.Id
                && message.Direction == Incoming
                && message.Sequence > (context.OrganizationConversationReadStates
                    .Where(state => state.ConversationId == item.Id && state.UserId == userId)
                    .Select(state => state.LastReadSequence)
                    .FirstOrDefault())));
        }

        var cursor = ParseInboxCursor(query.Cursor);
        if (cursor is not null)
        {
            source = source.Where(item => item.LastMessageAt < cursor.Value.LastMessageAt
                || (item.LastMessageAt == cursor.Value.LastMessageAt && item.Id.CompareTo(cursor.Value.Id) < 0));
        }

        var unreadCount = await CountUnreadMessagesAsync(userId, organizationId, cancellationToken);
        var rows = await source
            .OrderByDescending(item => item.LastMessageAt)
            .ThenByDescending(item => item.Id)
            .Take(pageSize + 1)
            .Select(item => new
            {
                item.Id,
                item.OrganizationClientId,
                ClientId = canViewClient ? item.OrganizationClientId : null,
                ClientName = canViewClient && item.OrganizationClient != null ? item.OrganizationClient.Name : null,
                item.BotId,
                item.BotUsername,
                item.ConnectionId,
                ChannelCode = item.Connection != null ? item.Connection.ProviderCode : "telegram",
                item.TelegramDisplayName,
                item.LastMessageText,
                item.LastMessageAt,
                item.IsReadOnly,
                LastMessageDirection = context.OrganizationConversationMessages
                    .Where(message => message.ConversationId == item.Id)
                    .OrderByDescending(message => message.Sequence)
                    .Select(message => message.Direction)
                    .FirstOrDefault(),
                ReadSequence = context.OrganizationConversationReadStates
                    .Where(state => state.ConversationId == item.Id && state.UserId == userId)
                    .Select(state => state.LastReadSequence)
                    .FirstOrDefault(),
                LastIncomingSequence = context.OrganizationConversationMessages
                    .Where(message => message.ConversationId == item.Id && message.Direction == Incoming)
                    .Select(message => (long?)message.Sequence)
                    .Max(),
                UnreadCount = context.OrganizationConversationMessages
                    .Count(message => message.ConversationId == item.Id
                        && message.Direction == Incoming
                        && message.Sequence > context.OrganizationConversationReadStates
                            .Where(state => state.ConversationId == item.Id && state.UserId == userId)
                            .Select(state => state.LastReadSequence)
                            .FirstOrDefault())
            })
            .ToListAsync(cancellationToken);
        var hasMore = rows.Count > pageSize;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        var items = rows.Select(item => new ConversationSummaryDto
        {
            Id = item.Id,
            ClientId = item.ClientId,
            ClientName = item.ClientName,
            BotId = item.BotId,
            BotUsername = item.BotUsername,
            TelegramDisplayName = item.TelegramDisplayName,
            ExternalDisplayName = item.TelegramDisplayName,
            ConnectionId = item.ConnectionId,
            ChannelCode = item.ChannelCode,
            LastMessageText = item.LastMessageText,
            LastMessageAt = item.LastMessageAt,
            LastMessageDirection = item.LastMessageDirection == null ? null : ParseDirection(item.LastMessageDirection),
            UnreadCount = item.UnreadCount,
            IsReadOnly = item.IsReadOnly
        }).ToList();
        var last = rows.LastOrDefault();

        return new ConversationInboxPageDto
        {
            Items = items,
            NextCursor = hasMore && last?.LastMessageAt is not null
                ? EncodeInboxCursor(last.LastMessageAt.Value, last.Id)
                : null,
            UnreadCount = unreadCount
        };
    }

    public async Task<ConversationClientInboxPageDto> GetClientInboxAsync(
        string userId,
        Guid organizationId,
        ConversationInboxQueryDto query,
        CancellationToken cancellationToken = default)
    {
        await access.EnsureViewAsync(userId, organizationId);
        await ReconcileBotStateAsync(organizationId, cancellationToken);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var search = query.Search?.Trim();
        var canViewClient = await access.HasPermissionAsync(userId, organizationId, "clients.view");
        if (query.ClientId.HasValue)
        {
            await access.EnsurePermissionAsync(userId, organizationId, "clients.view");
        }

        var source = context.OrganizationConversations.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && item.LastMessageAt != null);
        if (query.ClientId.HasValue)
        {
            source = source.Where(item => item.OrganizationClientId == query.ClientId.Value);
        }

        if (!string.IsNullOrEmpty(search))
        {
            source = source.Where(item =>
                (item.TelegramDisplayName != null && EF.Functions.ILike(item.TelegramDisplayName, $"%{search}%"))
                || (item.TelegramUsername != null && EF.Functions.ILike(item.TelegramUsername, $"%{search}%"))
                || (item.LastMessageText != null && EF.Functions.ILike(item.LastMessageText, $"%{search}%"))
                || (canViewClient && item.OrganizationClient != null && EF.Functions.ILike(item.OrganizationClient.Name, $"%{search}%")));
        }

        if (query.OnlyUnread)
        {
            source = source.Where(item => context.OrganizationConversationMessages.Any(message =>
                message.ConversationId == item.Id
                && message.Direction == Incoming
                && message.Sequence > context.OrganizationConversationReadStates
                    .Where(state => state.ConversationId == item.Id && state.UserId == userId)
                    .Select(state => state.LastReadSequence)
                    .FirstOrDefault()));
        }

        var cursor = ParseInboxCursor(query.Cursor);
        var groups = source
            .GroupBy(item => new
            {
                IsClientGroup = canViewClient && item.OrganizationClientId.HasValue,
                GroupId = canViewClient && item.OrganizationClientId.HasValue
                    ? item.OrganizationClientId!.Value
                    : item.Id
            })
            .Select(group => new
            {
                group.Key.GroupId,
                group.Key.IsClientGroup,
                LastMessageAt = group.Max(item => item.LastMessageAt),
                LastConversationId = group.OrderByDescending(item => item.LastMessageAt)
                    .ThenByDescending(item => item.Id)
                    .Select(item => item.Id)
                    .First(),
                UnreadCount = group.Sum(item => context.OrganizationConversationMessages.Count(message =>
                    message.ConversationId == item.Id
                    && message.Direction == Incoming
                    && message.Sequence > context.OrganizationConversationReadStates
                        .Where(state => state.ConversationId == item.Id && state.UserId == userId)
                        .Select(state => state.LastReadSequence)
                        .FirstOrDefault()))
            });
        if (cursor is not null)
        {
            groups = groups.Where(group => group.LastMessageAt < cursor.Value.LastMessageAt
                || (group.LastMessageAt == cursor.Value.LastMessageAt && group.LastConversationId.CompareTo(cursor.Value.Id) < 0));
        }

        var page = await groups
            .OrderByDescending(group => group.LastMessageAt)
            .ThenByDescending(group => group.LastConversationId)
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken);
        var hasMore = page.Count > pageSize;
        if (hasMore)
        {
            page.RemoveAt(page.Count - 1);
        }

        var clientIds = page.Where(group => group.IsClientGroup).Select(group => group.GroupId).ToArray();
        var unknownConversationIds = page.Where(group => !group.IsClientGroup).Select(group => group.GroupId).ToArray();
        if (clientIds.Length > 0)
        {
            await EnsureClientConversationRoutesAsync(organizationId, clientIds, cancellationToken);
        }

        var routeRows = await context.OrganizationConversations.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId
                && ((canViewClient && item.OrganizationClientId.HasValue && clientIds.Contains(item.OrganizationClientId.Value))
                    || (item.LastMessageAt != null && unknownConversationIds.Contains(item.Id))))
            .Select(item => new
            {
                item.Id,
                item.OrganizationClientId,
                ClientName = canViewClient && item.OrganizationClient != null ? item.OrganizationClient.Name : null,
                item.BotId,
                item.BotUsername,
                item.ConnectionId,
                ChannelCode = item.Connection != null ? item.Connection.ProviderCode : Telegram,
                item.TelegramDisplayName,
                item.LastMessageText,
                item.LastMessageAt,
                IsReadOnly = item.IsReadOnly || !item.IsChatActive || (item.Connection != null && !item.Connection.IsCurrent),
                LastMessageDirection = context.OrganizationConversationMessages
                    .Where(message => message.ConversationId == item.Id)
                    .OrderByDescending(message => message.Sequence)
                    .Select(message => message.Direction)
                    .FirstOrDefault(),
                UnreadCount = context.OrganizationConversationMessages.Count(message =>
                    message.ConversationId == item.Id
                    && message.Direction == Incoming
                    && message.Sequence > context.OrganizationConversationReadStates
                        .Where(state => state.ConversationId == item.Id && state.UserId == userId)
                        .Select(state => state.LastReadSequence)
                        .FirstOrDefault())
            })
            .ToListAsync(cancellationToken);
        var routes = routeRows.Select(item => new ConversationSummaryDto
        {
            Id = item.Id,
            ClientId = canViewClient ? item.OrganizationClientId : null,
            ClientName = item.ClientName,
            BotId = item.BotId,
            BotUsername = item.BotUsername,
            TelegramDisplayName = item.TelegramDisplayName,
            ExternalDisplayName = item.TelegramDisplayName,
            ConnectionId = item.ConnectionId,
            ChannelCode = item.ChannelCode,
            LastMessageText = item.LastMessageText,
            LastMessageAt = item.LastMessageAt,
            LastMessageDirection = item.LastMessageDirection is null ? null : ParseDirection(item.LastMessageDirection),
            UnreadCount = item.UnreadCount,
            IsReadOnly = item.IsReadOnly
        }).ToDictionary(item => item.Id);
        var items = page.Select(group =>
        {
            IReadOnlyList<ConversationSummaryDto> groupRoutes = group.IsClientGroup
                ? routeRows.Where(route => route.OrganizationClientId == group.GroupId)
                    .Select(route => routes[route.Id])
                    .OrderBy(route => route.ChannelCode)
                    .ThenBy(route => route.Id)
                    .ToList()
                : routes.TryGetValue(group.GroupId, out var unknownRoute)
                    ? new List<ConversationSummaryDto> { unknownRoute }
                    : Array.Empty<ConversationSummaryDto>();
            routes.TryGetValue(group.LastConversationId, out var latestRoute);

            return new ConversationClientInboxGroupDto
            {
                GroupId = group.GroupId,
                ClientId = group.IsClientGroup ? group.GroupId : null,
                ClientName = group.IsClientGroup ? groupRoutes.Select(route => route.ClientName).FirstOrDefault(name => name is not null) : null,
                ExternalDisplayName = group.IsClientGroup ? null : latestRoute?.ExternalDisplayName,
                LastMessageText = latestRoute?.LastMessageText,
                LastMessageAt = group.LastMessageAt,
                UnreadCount = group.UnreadCount,
                Routes = groupRoutes
            };
        }).ToList();
        var unreadCount = await CountUnreadMessagesAsync(userId, organizationId, cancellationToken);
        var last = page.LastOrDefault();

        return new ConversationClientInboxPageDto
        {
            Items = items,
            NextCursor = hasMore && last?.LastMessageAt is not null
                ? EncodeInboxCursor(last.LastMessageAt.Value, last.LastConversationId)
                : null,
            UnreadCount = unreadCount
        };
    }

    public async Task<ConversationUnreadSummaryDto> GetUnreadSummaryAsync(
        string userId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        await access.EnsureViewAsync(userId, organizationId);
        await ReconcileBotStateAsync(organizationId, cancellationToken);
        var unreadCount = await CountUnreadMessagesAsync(userId, organizationId, cancellationToken);
        var lastChanged = await context.OrganizationConversations.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId)
            .Select(item => (DateTime?)item.UpdatedAt)
            .MaxAsync(cancellationToken);

        return new ConversationUnreadSummaryDto
        {
            UnreadCount = unreadCount,
            Revision = lastChanged?.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "0"
        };
    }

    public Task<ConversationDeliveryHealthDto> GetDeliveryHealthForOrganizationAsync(
        string userId,
        Guid organizationId,
        CancellationToken cancellationToken = default) =>
        deliveryService.GetDeliveryHealthForOrganizationAsync(userId, organizationId, cancellationToken);

    public async Task<ConversationMessagesPageDto?> GetMessagesAsync(
        string userId,
        Guid organizationId,
        Guid conversationId,
        long? beforeSequence = null,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        await access.EnsureViewAsync(userId, organizationId);
        await ReconcileBotStateAsync(organizationId, cancellationToken);
        var conversation = await context.OrganizationConversations.AsNoTracking()
            .Include(item => item.OrganizationClient)
            .Include(item => item.Connection)
            .Where(item => item.Id == conversationId && item.OrganizationId == organizationId)
            .FirstOrDefaultAsync(cancellationToken);
        if (conversation is null)
        {
            return null;
        }

        var take = Math.Clamp(pageSize, 1, 100);
        var query = context.OrganizationConversationMessages.AsNoTracking()
            .Where(item => item.ConversationId == conversationId);
        if (beforeSequence.HasValue)
        {
            query = query.Where(item => item.Sequence < beforeSequence.Value);
        }

        var rows = await query.OrderByDescending(item => item.Sequence)
            .Take(take + 1)
            .Include(item => item.Attachments)
            .Include(item => item.Maintenance)
            .ToListAsync(cancellationToken);
        var hasMore = rows.Count > take;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        var isReadOnly = conversation.IsReadOnly || !conversation.IsChatActive
            || !await routeAvailability.IsCurrentAsync(conversation, cancellationToken);
        var isWorkspaceReadOnly = await access.IsWorkspaceReadOnlyAsync(userId, organizationId);
        var hasMessaging = await access.CanUseClientMessagingAsync(userId, organizationId);
        var canSend = await access.HasPermissionAsync(userId, organizationId, "messages.send")
            && hasMessaging && !isWorkspaceReadOnly && !isReadOnly;
        var canManage = await access.HasPermissionAsync(userId, organizationId, "messages.manage")
            && hasMessaging && !isWorkspaceReadOnly;
        var canViewClient = await access.HasPermissionAsync(userId, organizationId, "clients.view");
        var canViewMaintenance = await access.HasPermissionAsync(userId, organizationId, "maintenance.view");
        return new ConversationMessagesPageDto
        {
            ConversationId = conversationId,
            Conversation = new ConversationSummaryDto
            {
                Id = conversation.Id,
                ClientId = canViewClient ? conversation.OrganizationClientId : null,
                ClientName = canViewClient ? conversation.OrganizationClient?.Name : null,
                BotId = conversation.BotId,
                BotUsername = conversation.BotUsername,
                TelegramDisplayName = conversation.TelegramDisplayName,
                ExternalDisplayName = conversation.TelegramDisplayName,
                ConnectionId = conversation.ConnectionId,
                ChannelCode = conversation.Connection?.ProviderCode ?? Telegram,
                LastMessageText = conversation.LastMessageText,
                LastMessageAt = conversation.LastMessageAt,
                UnreadCount = await CountUnreadConversationMessagesAsync(userId, conversation.Id, cancellationToken),
                IsReadOnly = isReadOnly
            },
            Items = rows.Select(item => MapMessage(item, canViewMaintenance)).ToList(),
            NextBeforeSequence = hasMore ? rows.LastOrDefault()?.Sequence : null,
            LastSequence = conversation.LastSequence,
            IsReadOnly = isReadOnly,
            CanSend = canSend,
            CanManage = canManage,
            CanViewClient = canViewClient,
            CanViewMaintenance = canViewMaintenance,
            TimeZoneId = await context.Organizations.AsNoTracking()
                .Where(item => item.Id == organizationId)
                .Select(item => item.ShiftTimeZoneId)
                .FirstOrDefaultAsync(cancellationToken)
        };
    }

    public async Task<ConversationMessagesPageDto?> GetForClientAsync(
        string userId,
        Guid organizationId,
        Guid clientId,
        CancellationToken cancellationToken = default)
    {
        var routes = await this.GetClientRoutesAsync(userId, organizationId, clientId, cancellationToken);
        if (routes is null)
        {
            return null;
        }

        var selectedRoute = routes.ActiveRoutes.Count == 1
            ? routes.ActiveRoutes[0]
            : routes.ActiveRoutes.FirstOrDefault(item => item.ChannelCode == Telegram)
                ?? (routes.ActiveRoutes.Count == 0 && routes.HistoryRoutes.Count == 1 ? routes.HistoryRoutes[0] : null);

        return selectedRoute is null
            ? null
            : await this.GetMessagesAsync(userId, organizationId, selectedRoute.ConversationId, cancellationToken: cancellationToken);
    }

    public async Task<ConversationClientRoutesDto?> GetClientRoutesAsync(
        string userId,
        Guid organizationId,
        Guid clientId,
        CancellationToken cancellationToken = default)
    {
        await access.EnsureViewAsync(userId, organizationId);
        await access.EnsurePermissionAsync(userId, organizationId, "clients.view");
        var client = await context.OrganizationClients.AsNoTracking()
            .Where(item => item.Id == clientId && item.OrganizationId == organizationId && !item.IsDeleted)
            .Select(item => new { item.Id, item.Name })
            .FirstOrDefaultAsync(cancellationToken);
        if (client is null)
        {
            return null;
        }

        await EnsureClientConversationRoutesAsync(organizationId, [clientId], cancellationToken);

        var routes = await context.OrganizationConversations.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && item.OrganizationClientId == clientId
                && item.ConnectionId.HasValue && item.ExternalConversationId != null)
            .OrderByDescending(item => item.LastMessageAt)
            .Select(item => new ConversationRouteRow
            {
                Id = item.Id,
                ConnectionId = item.ConnectionId!.Value,
                ChannelCode = item.Connection!.ProviderCode,
                ChannelName = item.Connection.Username,
                ExternalConversationId = item.ExternalConversationId,
                IsChatActive = item.IsChatActive,
                IsReadOnly = item.IsReadOnly,
                LastMessageAt = item.LastMessageAt,
                IsVerified = context.OrganizationClientChannelLinks.Any(link =>
                    link.OrganizationId == organizationId
                    && link.ClientId == clientId
                    && link.ConnectionId == item.ConnectionId
                    && link.ExternalConversationId == item.ExternalConversationId
                    && link.IsActive),
                IsCurrent = item.Connection!.IsCurrent,
                UnreadCount = context.OrganizationConversationMessages.Count(message =>
                    message.ConversationId == item.Id
                    && message.Direction == Incoming
                    && message.Sequence > context.OrganizationConversationReadStates
                        .Where(state => state.ConversationId == item.Id && state.UserId == userId)
                        .Select(state => state.LastReadSequence)
                        .FirstOrDefault())
            })
            .ToListAsync(cancellationToken);
        var activeRoutes = routes
            .Where(item => item.IsVerified && item.IsCurrent && item.IsChatActive && !item.IsReadOnly)
            .Select(MapRoute)
            .ToArray();
        var historyRoutes = routes
            .Where(item => !item.IsVerified || !item.IsCurrent || !item.IsChatActive || item.IsReadOnly)
            .Select(MapRoute)
            .ToArray();

        return new ConversationClientRoutesDto
        {
            ClientId = client.Id,
            DisplayName = client.Name,
            ActiveRoutes = activeRoutes,
            HistoryRoutes = historyRoutes
        };
    }

    private static ConversationChannelRouteDto MapRoute(ConversationRouteRow item) => new()
    {
        ConversationId = item.Id,
        ConnectionId = item.ConnectionId,
        ChannelCode = item.ChannelCode,
        DisplayName = item.ChannelName ?? item.ChannelCode,
        IsReadOnly = !item.IsVerified || !item.IsCurrent || !item.IsChatActive || item.IsReadOnly,
        UnreadCount = item.UnreadCount,
        LastMessageAt = item.LastMessageAt
    };

    private async Task EnsureClientConversationRoutesAsync(
        Guid organizationId,
        IReadOnlyCollection<Guid> clientIds,
        CancellationToken cancellationToken)
    {
        if (clientIds.Count == 0)
        {
            return;
        }

        var clientLinks = await context.OrganizationClientChannelLinks.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId
                && clientIds.Contains(item.ClientId)
                && item.IsActive
                && item.Connection.IsCurrent)
            .OrderBy(item => item.ClientId)
            .ThenBy(item => item.ConnectionId)
            .ThenBy(item => item.ExternalConversationId)
            .Select(item => new
            {
                item.ClientId,
                item.ConnectionId,
                item.ExternalConversationId,
                item.ExternalUserId,
                item.Connection.ProviderCode,
                item.Connection.ImmutableProviderIdentity,
                item.Connection.Username
            })
            .ToListAsync(cancellationToken);
        foreach (var link in clientLinks)
        {
            var deliveryAddress = link.ProviderCode == Max ? link.ExternalUserId : link.ExternalConversationId;
            var telegramBotId = link.ProviderCode == Telegram
                ? long.Parse(link.ImmutableProviderIdentity, System.Globalization.CultureInfo.InvariantCulture)
                : (long?)null;
            var telegramChatId = link.ProviderCode == Telegram
                ? long.Parse(link.ExternalConversationId, System.Globalization.CultureInfo.InvariantCulture)
                : (long?)null;
            var telegramUserId = link.ProviderCode == Telegram
                ? long.Parse(link.ExternalUserId, System.Globalization.CultureInfo.InvariantCulture)
                : (long?)null;
            var now = DateTime.UtcNow;
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "OrganizationConversations" (
                    "Id", "OrganizationId", "ConnectionId", "ExternalConversationId", "DeliveryAddress",
                    "BotId", "BotUsername", "TelegramChatId", "TelegramUserId", "OrganizationClientId",
                    "LastSequence", "IsReadOnly", "IsChatActive", "CreatedAt", "UpdatedAt")
                VALUES (
                    {Guid.NewGuid()}, {organizationId}, {link.ConnectionId}, {link.ExternalConversationId}, {deliveryAddress},
                    {telegramBotId}, {link.Username ?? string.Empty}, {telegramChatId}, {telegramUserId}, {link.ClientId},
                    0, FALSE, TRUE, {now}, {now})
                ON CONFLICT ("ConnectionId", "ExternalConversationId")
                    WHERE "ConnectionId" IS NOT NULL AND "ExternalConversationId" IS NOT NULL DO NOTHING
                """, cancellationToken);
        }
    }

    private sealed record ConversationRouteRow
    {
        public Guid Id { get; init; }
        public Guid ConnectionId { get; init; }
        public string ChannelCode { get; init; } = string.Empty;
        public string? ChannelName { get; init; }
        public string? ExternalConversationId { get; init; }
        public bool IsChatActive { get; init; }
        public bool IsReadOnly { get; init; }
        public DateTime? LastMessageAt { get; init; }
        public bool IsVerified { get; init; }
        public bool IsCurrent { get; init; }
        public int UnreadCount { get; init; }
    }

    public Task<ConversationMessageDto> SendAsync(
        string userId,
        Guid organizationId,
        Guid conversationId,
        SendConversationMessageDto request,
        CancellationToken cancellationToken = default) =>
        ExecuteWithExecutionStrategyAsync(() => SendCoreAsync(userId, organizationId, conversationId, request, cancellationToken));

    private async Task<ConversationMessageDto> SendCoreAsync(
        string userId,
        Guid organizationId,
        Guid conversationId,
        SendConversationMessageDto request,
        CancellationToken cancellationToken)
    {
        await access.EnsureSendAsync(userId, organizationId);
        if (request.RequestId == Guid.Empty)
        {
            throw new ArgumentException("RequestId обязателен для безопасного повтора отправки.", nameof(request));
        }

        if (request.MaintenanceId.HasValue)
        {
            await access.EnsurePermissionAsync(userId, organizationId, "maintenance.view");
        }

        var attachmentIds = request.AttachmentIds ?? [];
        var text = request.Text?.Trim();
        if (string.IsNullOrEmpty(text) && attachmentIds.Count == 0)
        {
            throw new ArgumentException("Укажите текст сообщения или вложение.", nameof(request));
        }

        if (request.MessageTemplateId.HasValue != request.MessageTemplateVersion.HasValue)
        {
            throw new ArgumentException("Идентификатор шаблона и его версия должны передаваться вместе.", nameof(request));
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var conversation = await context.OrganizationConversations
            .Include(item => item.Connection)
            .SingleOrDefaultAsync(item => item.Id == conversationId && item.OrganizationId == organizationId, cancellationToken);
        if (conversation is null)
        {
            throw new KeyNotFoundException("Диалог не найден.");
        }

        await LockConversationAsync(conversationId, cancellationToken);
        await context.Entry(conversation).ReloadAsync(cancellationToken);

        var duplicate = await context.OrganizationConversationMessages.AsNoTracking()
            .Include(item => item.Attachments)
            .FirstOrDefaultAsync(item => item.ActorUserId == userId && item.RequestId == request.RequestId, cancellationToken);
        if (duplicate is not null)
        {
            if (duplicate.ConversationId != conversationId)
            {
                throw new InvalidOperationException("RequestId уже использован в другом диалоге.");
            }

            var duplicateAttachmentIds = duplicate.Attachments.Select(item => item.Id).Order().ToArray();
            var requestedAttachmentIds = attachmentIds.Order().ToArray();
            if (duplicate.Text != text || duplicate.MaintenanceId != request.MaintenanceId
                || duplicate.MessageTemplateId != request.MessageTemplateId
                || duplicate.MessageTemplateVersion != request.MessageTemplateVersion
                || !duplicateAttachmentIds.SequenceEqual(requestedAttachmentIds))
            {
                throw new InvalidOperationException("RequestId уже использован для другого содержимого сообщения.");
            }

            await transaction.CommitAsync(cancellationToken);

            return MapMessage(duplicate);
        }

        var maximumTextLength = conversation.Connection?.ProviderCode == Max
            ? 4000
            : attachmentIds.Count > 0 ? 1024 : 4096;
        if (text?.Length > maximumTextLength)
        {
            throw new ArgumentException($"Длина текста не должна превышать {maximumTextLength} символов для этого канала.", nameof(request));
        }

        if (request.MessageTemplateId.HasValue
            && !await context.MessageTemplates.AsNoTracking().AnyAsync(template =>
                template.Id == request.MessageTemplateId.Value
                && template.OrganizationId == organizationId
                && template.Version == request.MessageTemplateVersion
                && !template.IsArchived
                && !template.Group.IsArchived, cancellationToken))
        {
            throw new ArgumentException("Шаблон изменился или был архивирован. Обновите предпросмотр.", nameof(request));
        }

        if (conversation.IsReadOnly || !conversation.IsChatActive
            || !await routeAvailability.IsCurrentAsync(conversation, cancellationToken))
        {
            throw new InvalidOperationException("Переписка доступна только для чтения, так как бот был заменён или отключён.");
        }

        if (request.MaintenanceId.HasValue)
        {
            await ValidateMaintenanceAsync(organizationId, conversation.OrganizationClientId, request.MaintenanceId.Value, cancellationToken);
        }

        var distinctAttachmentIds = attachmentIds.Distinct().ToArray();
        if (distinctAttachmentIds.Length != attachmentIds.Count)
        {
            throw new ArgumentException("Список вложений содержит повторяющиеся идентификаторы.", nameof(request));
        }

        var attachments = distinctAttachmentIds.Length == 0
            ? []
            : await context.OrganizationConversationAttachments
                .Where(item => item.OrganizationId == organizationId
                    && item.ConversationId == conversationId
                    && item.UploadedByUserId == userId
                    && item.MessageId == null
                    && item.SizeBytes <= 20 * 1024 * 1024
                    && distinctAttachmentIds.Contains(item.Id))
                .OrderBy(item => item.Id)
                .ToListAsync(cancellationToken);
        if (attachments.Count != distinctAttachmentIds.Length || distinctAttachmentIds.Length > 1)
        {
            throw new UnauthorizedAccessException("Одно или несколько вложений недоступны для отправки.");
        }

        var now = DateTime.UtcNow;
        var message = new OrganizationConversationMessage
        {
            ConversationId = conversationId,
            Sequence = conversation.LastSequence + 1,
            Direction = Outgoing,
            Status = Queued,
            Text = text,
            ActorUserId = userId,
            ActorDisplayName = await GetUserDisplayNameAsync(userId, cancellationToken),
            MaintenanceId = request.MaintenanceId,
            MessageTemplateId = request.MessageTemplateId,
            MessageTemplateVersion = request.MessageTemplateVersion,
            RequestId = request.RequestId,
            CreatedAt = now,
            Attachments = attachments
        };
        foreach (var attachment in attachments)
        {
            attachment.MessageId = message.Id;
            attachment.UploadedByUserId = null;
        }

        conversation.LastSequence = message.Sequence;
        conversation.LastMessageText = text;
        conversation.LastMessageAt = now;
        conversation.UpdatedAt = NextRevision(conversation.UpdatedAt);
        context.OrganizationConversationMessages.Add(message);
        context.OrganizationConversationDispatches.Add(new OrganizationConversationDispatch
        {
            MessageId = message.Id,
            RequestId = request.RequestId,
            CreatedAt = now
        });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return MapMessage(message);
    }

    public Task<bool> MarkReadAsync(
        string userId,
        Guid organizationId,
        Guid conversationId,
        long throughSequence,
        CancellationToken cancellationToken = default) =>
        ExecuteWithExecutionStrategyAsync(() => MarkReadCoreAsync(userId, organizationId, conversationId, throughSequence, cancellationToken));

    private async Task<bool> MarkReadCoreAsync(
        string userId,
        Guid organizationId,
        Guid conversationId,
        long throughSequence,
        CancellationToken cancellationToken)
    {
        await access.EnsureViewAsync(userId, organizationId);
        var conversation = await context.OrganizationConversations
            .SingleOrDefaultAsync(item => item.Id == conversationId && item.OrganizationId == organizationId, cancellationToken);
        if (conversation is null)
        {
            return false;
        }

        var lastIncomingSequence = await context.OrganizationConversationMessages
            .Where(item => item.ConversationId == conversationId && item.Direction == Incoming && item.Sequence <= throughSequence)
            .Select(item => (long?)item.Sequence)
            .MaxAsync(cancellationToken) ?? 0;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "OrganizationConversationReadStates" ("ConversationId", "UserId", "LastReadSequence", "UpdatedAt")
            VALUES ({conversationId}, {userId}, {lastIncomingSequence}, {DateTime.UtcNow})
            ON CONFLICT ("ConversationId", "UserId") DO UPDATE
            SET "LastReadSequence" = GREATEST("OrganizationConversationReadStates"."LastReadSequence", EXCLUDED."LastReadSequence"),
                "UpdatedAt" = EXCLUDED."UpdatedAt"
            """, cancellationToken);
        conversation.UpdatedAt = NextRevision(conversation.UpdatedAt);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    public async Task<bool> LinkClientAsync(
        string userId,
        Guid organizationId,
        Guid conversationId,
        Guid? clientId,
        CancellationToken cancellationToken = default)
    {
        var isFirstAttempt = true;
        var strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            if (!isFirstAttempt)
            {
                context.ChangeTracker.Clear();
            }

            isFirstAttempt = false;

            return await LinkClientCoreAsync(userId, organizationId, conversationId, clientId, cancellationToken);
        });
    }

    private async Task<bool> LinkClientCoreAsync(
        string userId,
        Guid organizationId,
        Guid conversationId,
        Guid? clientId,
        CancellationToken cancellationToken)
    {
        await access.EnsureManageAsync(userId, organizationId);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        _ = await context.Organizations
            .FromSqlInterpolated($"SELECT * FROM \"Organizations\" WHERE \"Id\" = {organizationId} FOR UPDATE")
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
        var conversation = await context.OrganizationConversations
            .FromSqlInterpolated($"SELECT * FROM \"OrganizationConversations\" WHERE \"Id\" = {conversationId} AND \"OrganizationId\" = {organizationId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (conversation is null)
        {
            return false;
        }

        await context.Entry(conversation).ReloadAsync(cancellationToken);
        if (conversation.OrganizationClientId == clientId)
        {
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        var client = clientId.HasValue
            ? await context.OrganizationClients.SingleOrDefaultAsync(item =>
                item.Id == clientId.Value && item.OrganizationId == organizationId && !item.IsDeleted, cancellationToken)
            : null;
        if (clientId.HasValue && client is null)
        {
            throw new ArgumentException("Клиент не найден в этой организации.", nameof(clientId));
        }

        var providerCode = conversation.ConnectionId.HasValue
            ? await context.OrganizationChannelConnections.AsNoTracking()
                .Where(item => item.Id == conversation.ConnectionId && item.OrganizationId == organizationId)
                .Select(item => item.ProviderCode)
                .SingleAsync(cancellationToken)
            : Telegram;
        var oldLinks = await context.OrganizationClientChannelLinks
            .Where(item => item.OrganizationId == organizationId && item.ConnectionId == conversation.ConnectionId
                && item.ExternalConversationId == conversation.ExternalConversationId && item.IsActive)
            .ToListAsync(cancellationToken);
        var externalUserId = oldLinks.FirstOrDefault()?.ExternalUserId
            ?? conversation.DeliveryAddress
            ?? conversation.TelegramUserId?.ToString(System.Globalization.CultureInfo.InvariantCulture)
            ?? conversation.ExternalConversationId;
        var hasNumericAddress = long.TryParse(externalUserId, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var address);
        if (client is not null && hasNumericAddress
            && (providerCode == Telegram && client.TelegramChatId.HasValue && client.TelegramChatId != address
                || providerCode == Max && client.MaxUserId.HasValue && client.MaxUserId != address))
        {
            throw new InvalidOperationException("Клиент уже связан с другим контактом в этом мессенджере.");
        }

        if (clientId.HasValue && await context.OrganizationClientChannelLinks.AnyAsync(item =>
            item.OrganizationId == organizationId && item.ClientId == clientId
            && item.Connection.ProviderCode == providerCode && item.IsActive
            && (item.ConnectionId != conversation.ConnectionId || item.ExternalConversationId != conversation.ExternalConversationId), cancellationToken))
        {
            throw new InvalidOperationException("У клиента уже есть подтверждённый контакт в этом канале. Используйте привязку через подтверждение клиента.");
        }

        if (await context.OrganizationConversationMessages.AnyAsync(item => item.ConversationId == conversationId
            && item.Direction == Outgoing && item.Status == Sending, cancellationToken))
        {
            throw new InvalidOperationException("Дождитесь завершения отправки сообщения перед изменением клиента.");
        }

        var now = DateTime.UtcNow;
        if (hasNumericAddress)
        {
            var previousClients = await context.OrganizationClients.Where(item =>
                    item.OrganizationId == organizationId && (!clientId.HasValue || item.Id != clientId.Value)
                    && (providerCode == Telegram ? item.TelegramChatId == address : item.MaxUserId == address))
                .ToListAsync(cancellationToken);
            foreach (var previousClient in previousClients)
            {
                if (providerCode == Telegram)
                {
                    previousClient.TelegramChatId = null;
                    previousClient.TelegramConnectedAt = null;
                    previousClient.TelegramNotificationsEnabled = false;
                    previousClient.TelegramLinkTokenHash = null;
                    previousClient.TelegramLinkExpiresAt = null;
                }
                else
                {
                    previousClient.MaxUserId = null;
                    previousClient.MaxConnectedAt = null;
                    previousClient.MaxNotificationsEnabled = false;
                    previousClient.MaxLinkTokenHash = null;
                    previousClient.MaxLinkExpiresAt = null;
                }
            }
        }

        var pendingMessages = await context.OrganizationConversationMessages
            .Where(item => item.ConversationId == conversationId && item.Direction == Outgoing
                && (item.Status == Queued || item.Status == Failed && item.SafeToRetry))
            .ToListAsync(cancellationToken);
        foreach (var message in pendingMessages)
        {
            message.Status = Cancelled;
            message.SafeToRetry = false;
            message.RetryAt = null;
            message.ErrorCode = "client-link-changed";
            message.ErrorMessage = "Отправка отменена после изменения связи с клиентом.";
        }

        foreach (var link in oldLinks)
        {
            link.IsActive = false;
            link.RevokedAt = now;
        }

        conversation.OrganizationClientId = clientId;
        conversation.ChannelStateChangedAt = now;
        conversation.UpdatedAt = NextRevision(conversation.UpdatedAt);
        await context.SaveChangesAsync(cancellationToken);
        if (clientId.HasValue && conversation.ConnectionId.HasValue && !string.IsNullOrWhiteSpace(conversation.ExternalConversationId)
            && !string.IsNullOrWhiteSpace(externalUserId))
        {
            context.OrganizationClientChannelLinks.Add(new OrganizationClientChannelLink
            {
                OrganizationId = organizationId,
                ClientId = clientId.Value,
                ConnectionId = conversation.ConnectionId.Value,
                ExternalUserId = externalUserId,
                ExternalConversationId = conversation.ExternalConversationId,
                VerificationSource = "manual_crm",
                VerifiedAt = now,
                IsActive = conversation.IsChatActive && !conversation.IsReadOnly
            });
            await context.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    public Task<bool> IngestTelegramMessageAsync(
        IngestTelegramMessageDto update,
        CancellationToken cancellationToken = default) =>
        ExecuteWithExecutionStrategyAsync(() => IngestTelegramMessageCoreAsync(update, cancellationToken));

    public Task SetTelegramChatActiveAsync(
        Guid organizationId,
        long botId,
        long chatId,
        bool isActive,
        DateTime occurredAt,
        CancellationToken cancellationToken = default) => ExecuteWithExecutionStrategyAsync(async () =>
    {
        if (botId <= 0 || chatId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(botId), "Идентификаторы Telegram должны быть положительными.");
        }

        var identity = botId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var connection = await context.OrganizationChannelConnections
            .AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && item.ProviderCode == Telegram
                && item.ImmutableProviderIdentity == identity)
            .Select(item => new { item.Id, item.IsCurrent })
            .SingleOrDefaultAsync(cancellationToken);
        if (connection is null)
        {
            return;
        }

        await channelService.SetChannelActiveAsync(organizationId, connection.Id,
            chatId.ToString(System.Globalization.CultureInfo.InvariantCulture), isActive, occurredAt, cancellationToken);
    });

    public Task<bool> BindTelegramClientAsync(
        Guid organizationId,
        long botId,
        long chatId,
        Guid clientId,
        DateTime occurredAt,
        CancellationToken cancellationToken = default) => context.Database.CurrentTransaction is not null
            ? BindTelegramClientCoreAsync(organizationId, botId, chatId, clientId, occurredAt, cancellationToken)
            : ExecuteWithExecutionStrategyAsync(async () =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
                var isBound = await BindTelegramClientCoreAsync(organizationId, botId, chatId, clientId, occurredAt, cancellationToken);
                if (!isBound)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return false;
                }

                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return true;
            });

    private async Task<bool> BindTelegramClientCoreAsync(
        Guid organizationId,
        long botId,
        long chatId,
        Guid clientId,
        DateTime occurredAt,
        CancellationToken cancellationToken)
    {
        if (botId <= 0 || chatId <= 0 || clientId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(botId), "Идентификаторы Telegram и клиента должны быть заполнены.");
        }

        var bot = await context.OrganizationTelegramBots.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && item.TelegramBotId == botId && item.IsEnabled)
            .Select(item => new { item.Username, item.UpdatedAt })
            .SingleOrDefaultAsync(cancellationToken);
        if (bot is null)
        {
            return false;
        }

        var connectionId = await channelService.EnsureConnectionAsync(new EnsureOrganizationChannelConnectionDto
        {
            OrganizationId = organizationId,
            ProviderCode = Telegram,
            ImmutableProviderIdentity = botId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Username = bot.Username,
            IsVerified = true,
            VerifiedAt = bot.UpdatedAt == default ? DateTime.UtcNow : bot.UpdatedAt
        }, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        return await channelService.BindClientAsync(
            organizationId,
            connectionId,
            chatId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            chatId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            clientId,
            occurredAt,
            cancellationToken);
    }

    private async Task<bool> IngestTelegramMessageCoreAsync(
        IngestTelegramMessageDto update,
        CancellationToken cancellationToken)
    {
        if (update.BotId <= 0 || update.ChatId <= 0 || update.ProviderMessageId <= 0 || update.UpdateId < 0
            || update.SenderTelegramUserId != update.ChatId)
        {
            throw new ArgumentException("Webhook не содержит личное сообщение Telegram с корректными идентификаторами.", nameof(update));
        }

        var bot = await context.OrganizationTelegramBots.AsNoTracking()
            .Where(item => item.OrganizationId == update.OrganizationId
                && item.TelegramBotId == update.BotId
                && item.IsEnabled)
            .Select(item => new { item.OrganizationId, item.TelegramBotId, item.Username, item.UpdatedAt })
            .FirstOrDefaultAsync(cancellationToken);
        if (bot is null)
        {
            return false;
        }

        var isSupportedOrganization = await context.Organizations.AsNoTracking()
            .AnyAsync(item => item.Id == update.OrganizationId
                && item.IsActive
                && !item.IsArchived
                && !item.IsDeleted
                && (item.PrimaryScenarioCode == OrganizationTypeCodes.Detailing
                    || item.PrimaryScenarioCode == OrganizationTypeCodes.ServiceStation), cancellationToken);
        if (!isSupportedOrganization)
        {
            return false;
        }

        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var connectionId = await channelService.EnsureConnectionAsync(new EnsureOrganizationChannelConnectionDto
        {
            OrganizationId = update.OrganizationId,
            ProviderCode = Telegram,
            ImmutableProviderIdentity = update.BotId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Username = update.BotUsername ?? bot.Username,
            IsVerified = true,
            VerifiedAt = bot.UpdatedAt == default ? DateTime.UtcNow : bot.UpdatedAt
        }, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        var accepted = await channelService.IngestMessageAsync(new ChannelMessageReceivedDto
        {
            OrganizationId = update.OrganizationId,
            ConnectionId = connectionId,
            ProviderCode = Telegram,
            ExternalConversationId = update.ChatId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ExternalUserId = update.SenderTelegramUserId!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ProviderUpdateId = update.UpdateId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ProviderMessageId = update.ProviderMessageId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            SenderDisplayName = update.SenderDisplayName,
            SenderUsername = update.SenderUsername,
            Text = update.Text,
            Attachments = update.Attachments.Select(item => new ChannelAttachmentReceivedDto
            {
                Kind = item.Kind,
                ProviderFileId = item.ProviderFileId,
                ProviderFileUniqueId = item.ProviderFileUniqueId,
                FileName = item.FileName,
                ContentType = item.ContentType,
                SizeBytes = item.SizeBytes
            }).ToList(),
            OccurredAt = update.SentAt
        }, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return accepted;
    }

    public Task RegisterVerifiedBotAsync(
        Guid organizationId,
        long botId,
        string username,
        CancellationToken cancellationToken = default) =>
        ExecuteWithExecutionStrategyAsync(() => RegisterVerifiedBotCoreAsync(organizationId, botId, username, cancellationToken));

    private async Task RegisterVerifiedBotCoreAsync(
        Guid organizationId,
        long botId,
        string username,
        CancellationToken cancellationToken)
    {
        if (botId <= 0 || string.IsNullOrWhiteSpace(username) || username.Length > 100)
        {
            throw new ArgumentException("Telegram вернул некорректную идентичность бота.", nameof(botId));
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var bot = await context.OrganizationTelegramBots
            .SingleOrDefaultAsync(item => item.OrganizationId == organizationId, cancellationToken)
            ?? throw new KeyNotFoundException("Настройки Telegram-бота организации не найдены.");
        bot.TelegramBotId = botId;
        bot.Username = username.Trim().TrimStart('@');
        bot.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        await channelService.EnsureConnectionAsync(new EnsureOrganizationChannelConnectionDto
        {
            OrganizationId = organizationId,
            ProviderCode = Telegram,
            ImmutableProviderIdentity = botId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Username = bot.Username,
            IsVerified = true,
            VerifiedAt = bot.UpdatedAt
        }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<int> CountUnreadMessagesAsync(string userId, Guid organizationId, CancellationToken cancellationToken) =>
        await context.OrganizationConversationMessages.AsNoTracking()
            .Where(message => message.Conversation.OrganizationId == organizationId
                && message.Direction == Incoming
                && message.Sequence > context.OrganizationConversationReadStates
                    .Where(state => state.ConversationId == message.ConversationId && state.UserId == userId)
                    .Select(state => state.LastReadSequence)
                    .FirstOrDefault())
            .CountAsync(cancellationToken);

    private async Task<int> CountUnreadConversationMessagesAsync(string userId, Guid conversationId, CancellationToken cancellationToken) =>
        await context.OrganizationConversationMessages.AsNoTracking()
            .Where(message => message.ConversationId == conversationId
                && message.Direction == Incoming
                && message.Sequence > context.OrganizationConversationReadStates
                    .Where(state => state.ConversationId == message.ConversationId && state.UserId == userId)
                    .Select(state => state.LastReadSequence)
                    .FirstOrDefault())
            .CountAsync(cancellationToken);

    private async Task ValidateMaintenanceAsync(Guid organizationId, Guid? clientId, Guid maintenanceId, CancellationToken cancellationToken)
    {
        if (!clientId.HasValue)
        {
            throw new ArgumentException("Сначала свяжите диалог с клиентом, чтобы добавить контекст обслуживания.", nameof(maintenanceId));
        }

        var isLinked = await context.Maintenance.AsNoTracking()
            .Where(item => item.Id == maintenanceId
                && (item.BuyerClientId.HasValue
                    ? item.BuyerClientId == clientId
                    : item.Vehicle.OrganizationClientId == clientId)
                && item.Vehicle.OrganizationId == organizationId)
            .AnyAsync(cancellationToken);
        if (!isLinked)
        {
            throw new ArgumentException("Обслуживание не принадлежит клиенту этого диалога и организации.", nameof(maintenanceId));
        }
    }

    private async Task ReconcileBotStateAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var currentBotId = await context.OrganizationTelegramBots.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && item.IsEnabled)
            .Select(item => item.TelegramBotId)
            .FirstOrDefaultAsync(cancellationToken);
        var changedAt = DateTime.UtcNow;

        var retiredConversations = context.OrganizationConversations
            .Where(item => item.OrganizationId == organizationId
                && !item.IsReadOnly
                && (!item.ConnectionId.HasValue || item.Connection!.ProviderCode == Telegram)
                && (!currentBotId.HasValue || item.BotId != currentBotId.Value));
        await retiredConversations.ExecuteUpdateAsync(setters => setters
            .SetProperty(item => item.IsReadOnly, true)
            .SetProperty(item => item.UpdatedAt, changedAt), cancellationToken);

        await context.OrganizationConversations
            .Where(item => item.OrganizationId == organizationId
                && item.ConnectionId.HasValue
                && item.Connection != null
                && item.Connection.ProviderCode != Telegram
                && !item.Connection.IsCurrent
                && !item.IsReadOnly)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.IsReadOnly, true)
                .SetProperty(item => item.UpdatedAt, changedAt), cancellationToken);

        if (!currentBotId.HasValue)
        {
            return;
        }

        await context.OrganizationConversations
            .Where(item => item.OrganizationId == organizationId
                && (!item.ConnectionId.HasValue || item.Connection!.ProviderCode == Telegram)
                && item.BotId == currentBotId.Value
                && item.IsReadOnly
                && item.IsChatActive)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.IsReadOnly, false)
                .SetProperty(item => item.UpdatedAt, changedAt), cancellationToken);
    }

    private async Task<string?> GetUserDisplayNameAsync(string userId, CancellationToken cancellationToken) =>
        await context.Users.AsNoTracking()
            .Where(item => item.Id == userId)
            .Select(item => item.UserName)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task LockConversationAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        await context.OrganizationConversations
            .FromSqlInterpolated($"SELECT * FROM \"OrganizationConversations\" WHERE \"Id\" = {conversationId} FOR UPDATE")
            .IgnoreAutoIncludes()
            .SingleAsync(cancellationToken);
    }

    private static ConversationMessageDto MapMessage(OrganizationConversationMessage message, bool canViewMaintenance = true) => new()
    {
        Id = message.Id,
        Sequence = message.Sequence,
        Direction = ParseDirection(message.Direction),
        Status = ParseStatus(message.Status),
        Text = message.Text,
        ActorUserId = message.ActorUserId,
        ActorDisplayName = message.ActorDisplayName,
        MaintenanceId = canViewMaintenance ? message.MaintenanceId : null,
        MaintenanceTitle = canViewMaintenance && message.Maintenance is not null
            ? $"Обслуживание от {message.Maintenance.Date:dd.MM.yyyy}"
            : null,
        ProviderMessageId = message.ProviderMessageId,
        DeliveryErrorCode = IsPublicErrorCode(message.ErrorCode) ? message.ErrorCode : message.ErrorCode is null ? null : "delivery-error",
        RetryAt = message.RetryAt,
        MessageTemplateId = message.MessageTemplateId,
        MessageTemplateVersion = message.MessageTemplateVersion,
        CreatedAt = message.CreatedAt,
        DeliveredAt = message.DeliveredAt,
        Attachments = message.Attachments.OrderBy(item => item.SortOrder).Select(MapAttachment).ToList()
    };

    private static bool IsPublicErrorCode(string? errorCode) => !string.IsNullOrWhiteSpace(errorCode)
        && errorCode.Length <= 100
        && errorCode.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static ConversationAttachmentDto MapAttachment(OrganizationConversationAttachment attachment) => new()
    {
        Id = attachment.Id,
        Kind = attachment.Kind,
        FileName = attachment.FileName,
        ContentType = attachment.ContentType,
        SizeBytes = attachment.SizeBytes,
        SortOrder = attachment.SortOrder
    };

    private static ConversationMessageDirection ParseDirection(string direction) => direction switch
    {
        Incoming => ConversationMessageDirection.Incoming,
        Outgoing => ConversationMessageDirection.Outgoing,
        _ => throw new InvalidOperationException("В базе сохранено неизвестное направление сообщения.")
    };

    private static ConversationMessageStatus ParseStatus(string status) => status switch
    {
        Queued => ConversationMessageStatus.Queued,
        Sending => ConversationMessageStatus.Sending,
        Accepted => ConversationMessageStatus.Accepted,
        Failed => ConversationMessageStatus.Failed,
        Uncertain => ConversationMessageStatus.Uncertain,
        Cancelled => ConversationMessageStatus.Cancelled,
        _ => throw new InvalidOperationException("В базе сохранён неизвестный статус сообщения.")
    };

    private static (DateTime LastMessageAt, Guid Id)? ParseInboxCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        try
        {
            var value = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = value.Split(':', 2);
            return parts.Length == 2
                ? (new DateTime(long.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc), Guid.ParseExact(parts[1], "N"))
                : throw new FormatException();
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or OverflowException)
        {
            throw new ArgumentException("Курсор списка переписок некорректен.", nameof(cursor), exception);
        }
    }

    private static string EncodeInboxCursor(DateTime lastMessageAt, Guid id) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{lastMessageAt.Ticks}:{id:N}"));

    private static DateTime NextRevision(DateTime previous)
    {
        var now = DateTime.UtcNow;
        return now > previous ? now : previous.AddTicks(1);
    }

    private Task<T> ExecuteWithExecutionStrategyAsync<T>(Func<Task<T>> operation) =>
        context.Database.CreateExecutionStrategy().ExecuteAsync(operation);

    private Task ExecuteWithExecutionStrategyAsync(Func<Task> operation) =>
        context.Database.CreateExecutionStrategy().ExecuteAsync(operation);

}
