using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Communications.Contracts;
using OpenCRM.Modules.Communications.Contracts.Dtos;

namespace OpenCRM.Modules.Communications;

internal sealed class OrganizationChannelService(
    OpenCRMContext context) : IOrganizationChannelService
{
    private const string Telegram = "telegram";
    private const string Max = "max";
    private const string Incoming = "incoming";
    private const string Accepted = "accepted";

    public Task<Guid> EnsureConnectionAsync(
        EnsureOrganizationChannelConnectionDto request,
        CancellationToken cancellationToken = default) =>
        ExecuteInTransactionAsync(
            () => EnsureConnectionCoreAsync(request, cancellationToken),
            cancellationToken);

    public Task<bool> IngestMessageAsync(
        ChannelMessageReceivedDto message,
        CancellationToken cancellationToken = default) =>
        ExecuteInTransactionAsync(
            () => IngestMessageCoreAsync(message, cancellationToken),
            cancellationToken);

    public Task<bool> SetChannelActiveAsync(
        Guid organizationId,
        Guid connectionId,
        string externalConversationId,
        bool isActive,
        DateTime occurredAt,
        CancellationToken cancellationToken = default) =>
        ExecuteInTransactionAsync(async () =>
        {
            var connection = await context.OrganizationChannelConnections.AsNoTracking()
                .Where(item => item.OrganizationId == organizationId && item.Id == connectionId)
                .Select(item => new { item.ProviderCode, item.IsCurrent })
                .FirstOrDefaultAsync(cancellationToken);
            if (connection is null)
            {
                return false;
            }

            var conversation = await LockConversationAsync(organizationId, connectionId, externalConversationId, cancellationToken);
            if (conversation is null || occurredAt <= conversation.ChannelStateChangedAt)
            {
                return false;
            }

            conversation.IsChatActive = isActive;
            conversation.IsReadOnly = !isActive || !connection.IsCurrent;
            conversation.ChannelStateChangedAt = occurredAt;
            conversation.UpdatedAt = NextRevision(conversation.UpdatedAt, occurredAt);

            return true;
        }, cancellationToken);

    public Task<bool> BindClientAsync(
        Guid organizationId,
        Guid connectionId,
        string externalConversationId,
        string externalUserId,
        Guid clientId,
        DateTime occurredAt,
        CancellationToken cancellationToken = default) =>
        ExecuteInTransactionAsync(
            () => BindClientCoreAsync(organizationId, connectionId, externalConversationId, externalUserId, clientId, occurredAt, cancellationToken),
            cancellationToken);

    public Task RevokeClientAsync(
        Guid organizationId,
        Guid clientId,
        string providerCode,
        DateTime occurredAt,
        CancellationToken cancellationToken = default) =>
        ExecuteInTransactionAsync(async () =>
        {
            ValidateProviderCode(providerCode);
            await LockOrganizationAsync(organizationId, cancellationToken);
            var eventAt = NormalizeUtc(occurredAt);
            var links = await context.OrganizationClientChannelLinks
                .Where(item => item.OrganizationId == organizationId && item.ClientId == clientId
                    && item.Connection.ProviderCode == providerCode)
                .ToListAsync(cancellationToken);
            var conversations = await context.OrganizationConversations.AsNoTracking()
                .Where(item => item.OrganizationId == organizationId && item.OrganizationClientId == clientId
                    && item.Connection != null && item.Connection.ProviderCode == providerCode)
                .Select(item => new { item.ConnectionId, item.ExternalConversationId })
                .ToListAsync(cancellationToken);
            var routes = links.Select(item => new
                {
                    ConnectionId = (Guid?)item.ConnectionId,
                    ExternalConversationId = (string?)item.ExternalConversationId
                })
                .Concat(conversations)
                .Distinct()
                .OrderBy(item => item.ConnectionId)
                .ThenBy(item => item.ExternalConversationId);
            foreach (var route in routes)
            {
                var conversation = await LockConversationAsync(
                    organizationId, route.ConnectionId!.Value, route.ExternalConversationId!, cancellationToken);
                if (conversation is null || conversation.OrganizationClientId != clientId)
                {
                    continue;
                }

                if (await context.OrganizationConversationMessages.AnyAsync(item =>
                    item.ConversationId == conversation.Id && item.Direction == "outgoing" && item.Status == "sending", cancellationToken))
                {
                    throw new InvalidOperationException("Дождитесь завершения отправки сообщения перед отключением клиента.");
                }

                var pendingMessages = await context.OrganizationConversationMessages
                    .Where(item => item.ConversationId == conversation.Id && item.Direction == "outgoing"
                        && (item.Status == "queued" || item.Status == "failed" && item.SafeToRetry))
                    .ToListAsync(cancellationToken);
                foreach (var message in pendingMessages)
                {
                    message.Status = "cancelled";
                    message.SafeToRetry = false;
                    message.RetryAt = null;
                    message.ErrorCode = "client-channel-disconnected";
                    message.ErrorMessage = "Отправка отменена после отключения клиента.";
                }

                conversation.IsChatActive = false;
                conversation.IsReadOnly = true;
                conversation.ChannelStateChangedAt = eventAt;
                conversation.UpdatedAt = NextRevision(conversation.UpdatedAt, eventAt);
            }

            foreach (var link in links)
            {
                link.IsActive = false;
                link.RevokedAt = eventAt;
            }

            return true;
        }, cancellationToken);

    private async Task<Guid> EnsureConnectionCoreAsync(
        EnsureOrganizationChannelConnectionDto request,
        CancellationToken cancellationToken)
    {
        ValidateProviderCode(request.ProviderCode);
        if (request.OrganizationId == Guid.Empty
            || !request.IsVerified
            || string.IsNullOrWhiteSpace(request.ImmutableProviderIdentity)
            || request.ImmutableProviderIdentity.Length > 128
            || request.VerifiedAt == default)
        {
            throw new ArgumentException("Для подключения требуется проверенная неизменяемая идентичность канала.", nameof(request));
        }

        var identity = request.ImmutableProviderIdentity.Trim();
        if (request.ProviderCode == Telegram
            && (!long.TryParse(identity, NumberStyles.None, CultureInfo.InvariantCulture, out var telegramBotId)
                || telegramBotId <= 0
                || !await IsTelegramIdentityCurrentAsync(request.OrganizationId, telegramBotId, cancellationToken)))
        {
            throw new UnauthorizedAccessException("Идентичность Telegram-бота не совпадает с активной проверенной настройкой.");
        }

        if (request.ProviderCode == Max
            && !await IsMaxIdentityCurrentAsync(request.OrganizationId, identity, cancellationToken))
        {
            throw new UnauthorizedAccessException("Идентичность MAX-бота не совпадает с активной проверенной настройкой.");
        }

        await LockOrganizationAsync(request.OrganizationId, cancellationToken);
        var changedAt = NormalizeUtc(request.VerifiedAt);
        var connections = await context.OrganizationChannelConnections
            .Where(item => item.OrganizationId == request.OrganizationId && item.ProviderCode == request.ProviderCode)
            .OrderBy(item => item.Id)
            .ToListAsync(cancellationToken);
        var hasRegisteredIdentity = connections.Count > 0;
        var current = connections.SingleOrDefault(item => item.ImmutableProviderIdentity == identity);
        foreach (var previous in connections.Where(item => item.IsCurrent && item.ImmutableProviderIdentity != identity))
        {
            previous.IsCurrent = false;
            previous.RetiredAt = changedAt;
            await RetireConnectionAsync(previous.Id, changedAt, cancellationToken);
        }

        if (current is null)
        {
            current = new OrganizationChannelConnection
            {
                OrganizationId = request.OrganizationId,
                ProviderCode = request.ProviderCode,
                ImmutableProviderIdentity = identity,
                CreatedAt = changedAt
            };
            context.OrganizationChannelConnections.Add(current);
        }

        current.Username = NormalizeUsername(request.Username);
        current.IsCurrent = true;
        current.RetiredAt = null;
        current.VerifiedAt = changedAt;
        if (!hasRegisteredIdentity)
        {
            await BackfillLegacyClientLinksAsync(current, changedAt, cancellationToken);
        }

        return current.Id;
    }

    private async Task BackfillLegacyClientLinksAsync(
        OrganizationChannelConnection connection,
        DateTime verifiedAt,
        CancellationToken cancellationToken)
    {
        var legacyClients = connection.ProviderCode == Telegram
            ? await context.OrganizationClients.AsNoTracking()
                .Where(item => item.OrganizationId == connection.OrganizationId
                    && !item.IsDeleted
                    && item.TelegramChatId.HasValue
                    && item.TelegramConnectedAt.HasValue)
                .Select(item => new
                {
                    item.Id,
                    ExternalUserId = item.TelegramChatId!.Value.ToString(),
                    ExternalConversationId = item.TelegramChatId!.Value.ToString(),
                    VerifiedAt = item.TelegramConnectedAt!.Value
                })
                .ToListAsync(cancellationToken)
            : await context.OrganizationClients.AsNoTracking()
                .Where(item => item.OrganizationId == connection.OrganizationId
                    && !item.IsDeleted
                    && item.MaxUserId.HasValue
                    && item.MaxConnectedAt.HasValue)
                .Select(item => new
                {
                    item.Id,
                    ExternalUserId = item.MaxUserId!.Value.ToString(),
                    ExternalConversationId = item.MaxUserId!.Value.ToString(),
                    VerifiedAt = item.MaxConnectedAt!.Value
                })
                .ToListAsync(cancellationToken);

        foreach (var client in legacyClients)
        {
            if (await context.OrganizationClientChannelLinks.AnyAsync(item => item.ConnectionId == connection.Id
                && item.ExternalUserId == client.ExternalUserId && item.IsActive, cancellationToken))
            {
                continue;
            }

            context.OrganizationClientChannelLinks.Add(new OrganizationClientChannelLink
            {
                OrganizationId = connection.OrganizationId,
                ClientId = client.Id,
                ConnectionId = connection.Id,
                ExternalUserId = client.ExternalUserId,
                ExternalConversationId = client.ExternalConversationId,
                VerificationSource = "legacy_client",
                VerifiedAt = NormalizeUtc(client.VerifiedAt == default ? verifiedAt : client.VerifiedAt),
                IsActive = true
            });
        }
    }

    private async Task<bool> IsTelegramIdentityCurrentAsync(Guid organizationId, long botId, CancellationToken cancellationToken)
    {
        if (context.OrganizationTelegramBots.Local.Any(bot => bot.OrganizationId == organizationId
            && bot.IsEnabled && bot.TelegramBotId == botId))
        {
            return true;
        }

        return await context.OrganizationTelegramBots.AnyAsync(bot =>
            bot.OrganizationId == organizationId && bot.IsEnabled && bot.TelegramBotId == botId, cancellationToken);
    }

    private async Task<bool> IsMaxIdentityCurrentAsync(Guid organizationId, string identity, CancellationToken cancellationToken)
    {
        if (context.OrganizationMaxBots.Local.Any(bot => bot.OrganizationId == organizationId
            && bot.IsEnabled && bot.ImmutableProviderBotId == identity))
        {
            return true;
        }

        return await context.OrganizationMaxBots.AnyAsync(bot =>
            bot.OrganizationId == organizationId && bot.IsEnabled && bot.ImmutableProviderBotId == identity, cancellationToken);
    }

    private async Task<bool> IngestMessageCoreAsync(ChannelMessageReceivedDto update, CancellationToken cancellationToken)
    {
        ValidateProviderCode(update.ProviderCode);
        if (update.OrganizationId == Guid.Empty
            || update.ConnectionId == Guid.Empty
            || string.IsNullOrWhiteSpace(update.ExternalConversationId)
            || string.IsNullOrWhiteSpace(update.ExternalUserId)
            || string.IsNullOrWhiteSpace(update.ProviderMessageId)
            || update.ExternalConversationId.Length > 128
            || update.ExternalUserId.Length > 128
            || update.ProviderMessageId.Length > 128
            || update.DeliveryAddress?.Length > 128
            || update.ProviderUpdateId?.Length > 128
            || update.OccurredAt == default
            || update.Attachments.Count > 10
            || update.Attachments.Any(item => item.SizeBytes < 0))
        {
            throw new ArgumentException("Входящее сообщение канала содержит некорректные идентификаторы или вложения.", nameof(update));
        }

        if (update.ProviderCode == Telegram
            && (!long.TryParse(update.ExternalConversationId, NumberStyles.None, CultureInfo.InvariantCulture, out var chatId)
                || chatId <= 0))
        {
            throw new ArgumentException("Для личного Telegram-диалога требуется положительный числовой chat ID.", nameof(update));
        }

        var connection = await context.OrganizationChannelConnections.AsNoTracking()
            .Where(item => item.OrganizationId == update.OrganizationId
                && item.Id == update.ConnectionId
                && item.ProviderCode == update.ProviderCode)
            .Select(item => new { item.Id, item.IsCurrent, item.Username })
            .FirstOrDefaultAsync(cancellationToken);
        if (connection is null || !connection.IsCurrent)
        {
            return false;
        }

        var deliveryAddress = string.IsNullOrWhiteSpace(update.DeliveryAddress)
            ? update.ProviderCode == Max ? update.ExternalUserId : update.ExternalConversationId
            : update.DeliveryAddress.Trim();
        var expectedDeliveryAddress = update.ProviderCode == Max
            ? update.ExternalUserId
            : update.ExternalConversationId;
        if (deliveryAddress != expectedDeliveryAddress)
        {
            throw new ArgumentException("Адрес доставки не совпадает с подтверждённым адресом диалога канала.", nameof(update));
        }

        var text = update.Text?.Trim();
        var textLimit = update.ProviderCode == Max ? 4000 : 4096;
        if (text?.Length > textLimit)
        {
            throw new ArgumentException("Текст входящего сообщения превышает ограничение канала.", nameof(update));
        }

        var occurredAt = NormalizeUtc(update.OccurredAt);
        var conversation = await LockConversationAsync(
            update.OrganizationId,
            update.ConnectionId,
            update.ExternalConversationId,
            cancellationToken);
        if (conversation is null)
        {
            var activeLink = await context.OrganizationClientChannelLinks.AsNoTracking()
                .Where(item => item.OrganizationId == update.OrganizationId
                    && item.ConnectionId == update.ConnectionId
                    && item.ExternalConversationId == update.ExternalConversationId
                    && item.ExternalUserId == update.ExternalUserId
                    && item.IsActive)
                .Select(item => item.ClientId)
                .FirstOrDefaultAsync(cancellationToken);
            if (activeLink == Guid.Empty && await context.OrganizationClientChannelLinks.AsNoTracking().AnyAsync(item =>
                item.OrganizationId == update.OrganizationId && item.ConnectionId == update.ConnectionId
                && item.ExternalConversationId == update.ExternalConversationId && !item.IsActive, cancellationToken))
            {
                return false;
            }
            long? telegramBotId = null;
            long? telegramChatId = null;
            if (update.ProviderCode == Telegram)
            {
                var identity = await context.OrganizationChannelConnections.AsNoTracking()
                    .Where(item => item.Id == update.ConnectionId)
                    .Select(item => item.ImmutableProviderIdentity)
                    .SingleAsync(cancellationToken);
                telegramBotId = long.Parse(identity, CultureInfo.InvariantCulture);
                telegramChatId = long.Parse(update.ExternalConversationId, CultureInfo.InvariantCulture);
            }

            var telegramUserId = update.ProviderCode == Telegram
                && long.TryParse(update.ExternalUserId, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedTelegramUserId)
                    ? parsedTelegramUserId
                    : (long?)null;
            var displayName = NormalizeDisplayName(update.SenderDisplayName);
            var username = NormalizeUsername(update.SenderUsername);
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "OrganizationConversations" (
                    "Id", "OrganizationId", "ConnectionId", "ExternalConversationId", "DeliveryAddress",
                    "BotId", "BotUsername", "TelegramChatId", "TelegramUserId", "TelegramDisplayName",
                    "TelegramUsername", "OrganizationClientId", "LastSequence", "IsReadOnly", "IsChatActive",
                    "CreatedAt", "UpdatedAt")
                VALUES (
                    {Guid.NewGuid()}, {update.OrganizationId}, {update.ConnectionId}, {update.ExternalConversationId}, {deliveryAddress},
                    {telegramBotId}, {connection.Username ?? string.Empty}, {telegramChatId}, {telegramUserId}, {displayName},
                    {username}, {(activeLink == Guid.Empty ? null : activeLink)}, 0, FALSE, TRUE, {occurredAt}, {occurredAt})
                ON CONFLICT ("ConnectionId", "ExternalConversationId")
                    WHERE "ConnectionId" IS NOT NULL AND "ExternalConversationId" IS NOT NULL DO NOTHING
                """, cancellationToken);
            conversation = await LockConversationAsync(
                update.OrganizationId,
                update.ConnectionId,
                update.ExternalConversationId,
                cancellationToken);
            if (conversation is null)
            {
                throw new InvalidOperationException("Не удалось получить диалог канала после его создания.");
            }
        }

        if (conversation.IsReadOnly || !conversation.IsChatActive)
        {
            return false;
        }

        if (await context.OrganizationConversationMessages.AnyAsync(message => message.ConversationId == conversation.Id
            && ((update.ProviderUpdateId != null && message.ProviderUpdateId == update.ProviderUpdateId)
                || message.ProviderMessageId == update.ProviderMessageId), cancellationToken))
        {
            return false;
        }

        var message = new OrganizationConversationMessage
        {
            ConversationId = conversation.Id,
            Sequence = conversation.LastSequence + 1,
            Direction = Incoming,
            Status = Accepted,
            Text = text,
            SenderExternalUserId = update.ExternalUserId,
            SenderTelegramUserId = update.ProviderCode == Telegram
                && long.TryParse(update.ExternalUserId, NumberStyles.None, CultureInfo.InvariantCulture, out var senderId)
                ? senderId
                : null,
            ProviderUpdateId = update.ProviderUpdateId,
            TelegramUpdateId = update.ProviderCode == Telegram
                && long.TryParse(update.ProviderUpdateId, NumberStyles.None, CultureInfo.InvariantCulture, out var telegramUpdateId)
                ? telegramUpdateId
                : null,
            ProviderMessageId = update.ProviderMessageId,
            CreatedAt = occurredAt
        };
        foreach (var (attachment, sortOrder) in update.Attachments.Select((item, index) => (item, index)))
        {
            message.Attachments.Add(new OrganizationConversationAttachment
            {
                OrganizationId = update.OrganizationId,
                ConversationId = conversation.Id,
                Kind = NormalizeKind(attachment.Kind),
                FileName = NormalizeFileName(attachment.FileName),
                ContentType = NormalizeContentType(attachment.ContentType),
                SizeBytes = attachment.SizeBytes ?? 0,
                ProviderFileId = NormalizeExternalId(attachment.ProviderFileId),
                ProviderFileUniqueId = NormalizeExternalId(attachment.ProviderFileUniqueId),
                SortOrder = sortOrder,
                CreatedAt = occurredAt
            });
        }

        conversation.LastSequence = message.Sequence;
        conversation.LastMessageText = text ?? string.Join(" ", message.Attachments.Select(item => item.Kind));
        conversation.LastMessageAt = occurredAt;
        conversation.TelegramDisplayName = NormalizeDisplayName(update.SenderDisplayName);
        conversation.TelegramUsername = NormalizeUsername(update.SenderUsername);
        conversation.UpdatedAt = NextRevision(conversation.UpdatedAt, occurredAt);
        context.OrganizationConversationMessages.Add(message);
        await context.SaveChangesAsync(cancellationToken);

        return true;
    }

    private async Task<bool> BindClientCoreAsync(
        Guid organizationId,
        Guid connectionId,
        string externalConversationId,
        string externalUserId,
        Guid clientId,
        DateTime occurredAt,
        CancellationToken cancellationToken)
    {
        if (organizationId == Guid.Empty || connectionId == Guid.Empty || clientId == Guid.Empty
            || string.IsNullOrWhiteSpace(externalConversationId) || externalConversationId.Length > 128
            || string.IsNullOrWhiteSpace(externalUserId) || externalUserId.Length > 128)
        {
            return false;
        }

        var connection = await context.OrganizationChannelConnections
            .SingleOrDefaultAsync(item => item.OrganizationId == organizationId && item.Id == connectionId, cancellationToken);
        if (connection is null || !connection.IsCurrent
            || !await context.OrganizationClients.AnyAsync(item =>
                item.Id == clientId && item.OrganizationId == organizationId && !item.IsDeleted, cancellationToken))
        {
            return false;
        }

        await LockOrganizationAsync(organizationId, cancellationToken);
        var conversation = await LockConversationAsync(organizationId, connectionId, externalConversationId, cancellationToken);
        if (conversation is not null && occurredAt <= conversation.ChannelStateChangedAt)
        {
            return false;
        }

        if (conversation is null)
        {
            var telegramChatId = connection.ProviderCode == Telegram
                ? long.Parse(externalConversationId, CultureInfo.InvariantCulture)
                : (long?)null;
            conversation = new OrganizationConversation
            {
                OrganizationId = organizationId,
                ConnectionId = connectionId,
                ExternalConversationId = externalConversationId,
                DeliveryAddress = connection.ProviderCode == Max ? externalUserId : externalConversationId,
                BotId = connection.ProviderCode == Telegram
                    ? long.Parse(connection.ImmutableProviderIdentity, CultureInfo.InvariantCulture)
                    : null,
                BotUsername = connection.Username ?? string.Empty,
                TelegramChatId = telegramChatId,
                TelegramUserId = telegramChatId,
                OrganizationClientId = clientId,
                LastSequence = 0,
                IsReadOnly = false,
                IsChatActive = true,
                ChannelStateChangedAt = occurredAt,
                CreatedAt = occurredAt,
                UpdatedAt = occurredAt
            };
            context.OrganizationConversations.Add(conversation);
        }

        var previousLinks = await context.OrganizationClientChannelLinks
            .Where(item => item.OrganizationId == organizationId
                && item.ClientId == clientId
                && item.Connection.ProviderCode == connection.ProviderCode
                && item.IsActive
                && (item.ConnectionId != connectionId || item.ExternalConversationId != externalConversationId))
            .OrderBy(item => item.ConnectionId)
            .ThenBy(item => item.ExternalConversationId)
            .ToListAsync(cancellationToken);
        foreach (var previousLink in previousLinks)
        {
            previousLink.IsActive = false;
            previousLink.RevokedAt = occurredAt;
            var previousConversation = await LockConversationAsync(
                organizationId,
                previousLink.ConnectionId,
                previousLink.ExternalConversationId,
                cancellationToken);
            if (previousConversation is null)
            {
                continue;
            }

            previousConversation.IsChatActive = false;
            previousConversation.IsReadOnly = true;
            previousConversation.ChannelStateChangedAt = occurredAt;
            previousConversation.UpdatedAt = NextRevision(previousConversation.UpdatedAt, occurredAt);
        }

        string? legacyConversationId;
        if (connection.ProviderCode == Telegram)
        {
            var legacyChatId = await context.OrganizationClients.AsNoTracking()
                .Where(item => item.OrganizationId == organizationId && item.Id == clientId)
                .Select(item => item.TelegramChatId)
                .SingleOrDefaultAsync(cancellationToken);
            legacyConversationId = legacyChatId?.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            var legacyUserId = await context.OrganizationClients.AsNoTracking()
                .Where(item => item.OrganizationId == organizationId && item.Id == clientId)
                .Select(item => item.MaxUserId)
                .SingleOrDefaultAsync(cancellationToken);
            legacyConversationId = legacyUserId?.ToString(CultureInfo.InvariantCulture);
        }
        if (!string.IsNullOrWhiteSpace(legacyConversationId) && legacyConversationId != externalConversationId)
        {
            var legacyConversationIds = await context.OrganizationConversations.AsNoTracking()
                .Where(item => item.OrganizationId == organizationId
                    && item.Connection != null
                    && item.Connection.ProviderCode == connection.ProviderCode
                    && item.ExternalConversationId == legacyConversationId
                    && item.IsChatActive)
                .Select(item => new { item.ConnectionId, item.ExternalConversationId })
                .ToListAsync(cancellationToken);
            foreach (var legacyConversation in legacyConversationIds.OrderBy(item => item.ConnectionId))
            {
                if (!legacyConversation.ConnectionId.HasValue)
                {
                    continue;
                }

                var previousConversation = await LockConversationAsync(
                    organizationId,
                    legacyConversation.ConnectionId.Value,
                    legacyConversation.ExternalConversationId!,
                    cancellationToken);
                if (previousConversation is null)
                {
                    continue;
                }

                previousConversation.IsChatActive = false;
                previousConversation.IsReadOnly = true;
                previousConversation.ChannelStateChangedAt = occurredAt;
                previousConversation.UpdatedAt = NextRevision(previousConversation.UpdatedAt, occurredAt);
            }
        }

        var currentLink = await context.OrganizationClientChannelLinks
            .SingleOrDefaultAsync(item => item.OrganizationId == organizationId
                && item.ConnectionId == connectionId
                && item.ExternalConversationId == externalConversationId
                && item.IsActive, cancellationToken);
        if (currentLink is null)
        {
            currentLink = new OrganizationClientChannelLink
            {
                OrganizationId = organizationId,
                ConnectionId = connectionId,
                ClientId = clientId,
                ExternalUserId = externalUserId,
                ExternalConversationId = externalConversationId,
                VerificationSource = "validated_start",
                VerifiedAt = occurredAt,
                IsActive = true
            };
            context.OrganizationClientChannelLinks.Add(currentLink);
        }
        else
        {
            currentLink.ClientId = clientId;
            currentLink.VerifiedAt = occurredAt;
            currentLink.VerificationSource = "validated_start";
        }

        conversation.OrganizationClientId = clientId;
        conversation.IsChatActive = true;
        conversation.IsReadOnly = false;
        conversation.ChannelStateChangedAt = occurredAt;
        conversation.UpdatedAt = NextRevision(conversation.UpdatedAt, occurredAt);

        return true;
    }

    private async Task<OrganizationConversation?> LockConversationAsync(
        Guid organizationId,
        Guid connectionId,
        string externalConversationId,
        CancellationToken cancellationToken) =>
        await context.OrganizationConversations
            .FromSqlInterpolated($"SELECT * FROM \"OrganizationConversations\" WHERE \"OrganizationId\" = {organizationId} AND \"ConnectionId\" = {connectionId} AND \"ExternalConversationId\" = {externalConversationId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    private async Task LockOrganizationAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        _ = await context.Organizations
            .FromSqlInterpolated($"SELECT * FROM \"Organizations\" WHERE \"Id\" = {organizationId} FOR UPDATE")
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task RetireConnectionAsync(Guid connectionId, DateTime occurredAt, CancellationToken cancellationToken)
    {
        var conversations = await context.OrganizationConversations
            .FromSqlInterpolated($"SELECT * FROM \"OrganizationConversations\" WHERE \"ConnectionId\" = {connectionId} AND \"IsChatActive\" = TRUE ORDER BY \"Id\" FOR UPDATE")
            .ToListAsync(cancellationToken);
        foreach (var conversation in conversations)
        {
            conversation.IsChatActive = false;
            conversation.IsReadOnly = true;
            conversation.ChannelStateChangedAt = occurredAt;
            conversation.UpdatedAt = NextRevision(conversation.UpdatedAt, occurredAt);
        }

        var links = await context.OrganizationClientChannelLinks
            .Where(item => item.ConnectionId == connectionId && item.IsActive)
            .ToListAsync(cancellationToken);
        foreach (var link in links)
        {
            link.IsActive = false;
            link.RevokedAt = occurredAt;
        }
    }

    private async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is not null)
        {
            return await action();
        }

        var strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            var result = await action();
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return result;
        });
    }

    private static void ValidateProviderCode(string providerCode)
    {
        if (providerCode is not Telegram and not Max)
        {
            throw new ArgumentException("Поддерживаются только каналы Telegram и MAX.", nameof(providerCode));
        }
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

    private static DateTime NextRevision(DateTime current, DateTime eventAt)
    {
        var now = DateTime.UtcNow;
        var next = current.AddTicks(1);
        if (eventAt > next)
        {
            next = eventAt;
        }

        return now > next ? now : next;
    }

    private static string? NormalizeUsername(string? value)
    {
        var normalized = value?.Trim().TrimStart('@');
        return string.IsNullOrEmpty(normalized) ? null : normalized[..Math.Min(normalized.Length, 128)];
    }

    private static string? NormalizeDisplayName(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, 300)];

    private static string NormalizeKind(string value) =>
        string.IsNullOrWhiteSpace(value) ? "file" : value.Trim()[..Math.Min(value.Trim().Length, 32)];

    private static string NormalizeFileName(string? value)
    {
        var name = Path.GetFileName(value ?? string.Empty).Trim();
        return name.Length == 0 ? "Вложение" : name[..Math.Min(name.Length, 255)];
    }

    private static string NormalizeContentType(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "application/octet-stream" : value.Trim()[..Math.Min(value.Trim().Length, 128)];

    private static string? NormalizeExternalId(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, 128)];
}
