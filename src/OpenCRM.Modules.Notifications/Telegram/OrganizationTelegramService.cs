using Microsoft.Extensions.Configuration;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Communications.Contracts;
using OpenCRM.Modules.Communications.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Notifications.Telegram;

/// <summary> Сервис настройки и обработки Telegram-уведомлений для организации. </summary>
public sealed class OrganizationTelegramService(
    OpenCRMContext context,
    IDataProtectionProvider dataProtectionProvider,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    IOrganizationSubscriptionService subscriptionService,
    IOrganizationConversationService conversationService,
    IOrganizationChannelService channelService,
    IOrganizationPermissionService? permissionService = null)
{
    /// <summary> Защититель данных для шифрования токена бота. </summary>
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector("OpenCRM.OrganizationTelegramBot.v1");

    /// <summary> Возвращает учётные данные бота Telegram для получателя. </summary>
    public async Task<string?> GetBotCredentialAsync(
        Guid organizationId,
        string notificationKey,
        string recipientChatId,
        CancellationToken cancellationToken)
    {
        if (!await this.HasClientMessagingAccessAsync(organizationId, cancellationToken))
        {
            return null;
        }

        if (!configuration.GetValue<bool>("TelegramOrganizationNotifications:Enabled")
            || string.IsNullOrWhiteSpace(notificationKey)
            || !long.TryParse(recipientChatId, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var chatId)
            || chatId <= 0)
        {
            return null;
        }

        var notification = await context.TelegramClientNotifications.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrganizationId == organizationId
                && x.NotificationKey == notificationKey, cancellationToken);
        if (notification == null || notification.ChatId != chatId
            || notification.Status is "failed" or "skipped" or "sent" or "simulated")
        {
            return null;
        }

        var client = await context.OrganizationClients.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == notification.ClientId && x.OrganizationId == organizationId, cancellationToken);
        if (client == null || client.IsDeleted || !client.TelegramNotificationsEnabled
            || client.TelegramChatId != notification.ChatId
            || client.TelegramConnectedAt == null
            || client.TelegramConnectedAt.Value > notification.CreatedAt)
        {
            return null;
        }

        var maintenance = await context.Maintenance.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == notification.MaintenanceId, cancellationToken);
        var vehicle = maintenance == null ? null : await context.Vehicles.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == maintenance.VehicleId, cancellationToken);
        if (maintenance == null || vehicle?.OrganizationId != organizationId
            || vehicle.OrganizationClientId != notification.ClientId
            || await context.TelegramClientNotifications.AsNoTracking().AnyAsync(x => x.MaintenanceId == notification.MaintenanceId
                && x.ClientId == notification.ClientId && x.Id > notification.Id
                && x.EventCode != MaintenanceVisitReminderRules.EventCode, cancellationToken)
            || notification.EventCode == MaintenanceVisitReminderRules.EventCode
                && !await VisitReminderDeliveryGuard.IsCurrentAsync(context, configuration, maintenance, organizationId, client,
                    notification.VisitStartsAtUtc, notification.NotificationKey, "telegram", cancellationToken)
            || notification.EventCode == "ready" && (!maintenance.ReadyForHandoverAt.HasValue || maintenance.HandedOverAt.HasValue)
            || notification.EventCode == "booking_cancelled" && maintenance.Status != OpenCRM.Domain.Enums.MaintenanceStatus.Cancelled
            || notification.EventCode is "booking_created" or "booking_changed"
                && maintenance.Status is not (OpenCRM.Domain.Enums.MaintenanceStatus.Planned
                    or OpenCRM.Domain.Enums.MaintenanceStatus.InProgress))
        {
            return null;
        }

        var bot = await (from configuredBot in context.OrganizationTelegramBots.AsNoTracking()
            join organization in context.Organizations.AsNoTracking()
                on configuredBot.OrganizationId equals organization.Id
            where configuredBot.OrganizationId == organizationId && configuredBot.IsEnabled
                && (notification.EventCode == "ready" ? configuredBot.NotifyReady : configuredBot.NotifyBooking)
                && organization.IsActive && !organization.IsDeleted && !organization.IsArchived
            select configuredBot.ProtectedToken).SingleOrDefaultAsync(cancellationToken);

        return bot == null ? null : this._protector.Unprotect(bot);
    }

    /// <summary> Возвращает настройки Telegram-уведомлений организации. </summary>
    public async Task<object> GetSettingsAsync(string userId, Guid organizationId)
    {
        await this.EnsureOwnerAsync(userId, organizationId);
        var bot = await context.OrganizationTelegramBots.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrganizationId == organizationId);

        return new
        {
            botUsername = bot?.Username,
            isConfigured = bot != null,
            isEnabled = bot?.IsEnabled ?? false,
            isAvailable = configuration.GetValue<bool>("TelegramOrganizationNotifications:Enabled"),
            notifyBooking = bot?.NotifyBooking ?? true,
            notifyReady = bot?.NotifyReady ?? true,
            webhookUrl = this.GetWebhookUrl(organizationId)
        };
    }

    /// <summary> Сохраняет настройки Telegram-уведомлений организации. </summary>
    public async Task SaveSettingsAsync(string userId, Guid organizationId, string? token, bool isEnabled, bool notifyBooking, bool notifyReady, CancellationToken cancellationToken)
    {
        await this.EnsureOwnerAsync(userId, organizationId);
        if (isEnabled || !string.IsNullOrWhiteSpace(token))
        {
            await this.EnsureClientMessagingAccessAsync(userId, organizationId);
        }

        if (isEnabled && !configuration.GetValue<bool>("TelegramOrganizationNotifications:Enabled"))
        {
            throw new InvalidOperationException("Клиентский Telegram ещё не включён в opencrm.example.invalid.");
        }

        if ((isEnabled || !string.IsNullOrWhiteSpace(token))
            && (configuration["TelegramOrganizationNotifications:RelayKey"] is not { Length: >= 32 }
                || string.IsNullOrEmpty(this.GetWebhookUrl(organizationId))))
        {
            throw new InvalidOperationException("Клиентский Telegram ещё не настроен в opencrm.example.invalid.");
        }

        var bot = await context.OrganizationTelegramBots.SingleOrDefaultAsync(x => x.OrganizationId == organizationId, cancellationToken);
        token = token?.Trim();
        if (bot == null && string.IsNullOrEmpty(token))
        {
            throw new InvalidOperationException("Укажите токен бота Telegram.");
        }

        if (bot != null && !isEnabled && string.IsNullOrEmpty(token))
        {
            bot.IsEnabled = false;
            bot.NotifyBooking = notifyBooking;
            bot.NotifyReady = notifyReady;
            bot.UpdatedAt = DateTime.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
            return;
        }

        var effectiveToken = string.IsNullOrEmpty(token) ? this._protector.Unprotect(bot!.ProtectedToken) : token;
        OrganizationTelegramIdentity identity;
        try
        {
            identity = await this.GetBotIdentityAsync(effectiveToken, cancellationToken);
            if (await context.OrganizationTelegramBots.AnyAsync(x =>
                x.OrganizationId != organizationId
                && (x.TelegramBotId == identity.Id || x.Username == identity.Username), cancellationToken))
            {
                throw new InvalidOperationException("Этот бот Telegram уже подключён к другой организации.");
            }
            var secret = bot == null || !string.IsNullOrEmpty(token)
                ? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_')
                : this._protector.Unprotect(bot.ProtectedWebhookSecret);
            await this.RegisterWebhookAsync(effectiveToken, organizationId, secret, cancellationToken);

            if (bot == null)
            {
                bot = new OrganizationTelegramBot
                {
                    OrganizationId = organizationId,
                    ProtectedWebhookSecret = this._protector.Protect(secret)
                };
                context.OrganizationTelegramBots.Add(bot);
            }

            if (bot.TelegramBotId != identity.Id)
            {
                var clients = await context.OrganizationClients
                    .Where(x => x.OrganizationId == organizationId && x.TelegramChatId != null)
                    .ToListAsync(cancellationToken);
                foreach (var client in clients)
                {
                    client.TelegramChatId = null;
                    client.TelegramConnectedAt = null;
                    client.TelegramNotificationsEnabled = false;
                }
            }

            bot.Username = identity.Username;
            bot.TelegramBotId = identity.Id;
            bot.ProtectedToken = this._protector.Protect(effectiveToken);
            bot.ProtectedWebhookSecret = this._protector.Protect(secret);
            bot.IsEnabled = isEnabled;
            bot.NotifyBooking = notifyBooking;
            bot.NotifyReady = notifyReady;
            bot.UpdatedAt = DateTime.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new InvalidOperationException("Telegram не подтвердил подключение бота. Проверьте токен и повторите попытку.");
        }
    }

    /// <summary> Возвращает статус привязки клиента к Telegram. </summary>
    public async Task<object> GetClientStatusAsync(string userId, Guid organizationId, Guid clientId)
    {
        await subscriptionService.EnsureWorkspaceCanReadAsync(userId, organizationId, OrganizationPermissionCodes.ClientsView);
        var client = await context.OrganizationClients.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == clientId && x.OrganizationId == organizationId && !x.IsDeleted)
            ?? throw new KeyNotFoundException("Клиент не найден.");
        var bot = await context.OrganizationTelegramBots.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrganizationId == organizationId);
        var recentNotifications = await context.TelegramClientNotifications.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.ClientId == clientId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new { x.Id, x.EventCode, x.Body, x.Status, x.CreatedAt, x.SentAt, x.ErrorCode })
            .ToListAsync();

        return new
        {
            isBotConnected = bot?.IsEnabled == true
                && configuration.GetValue<bool>("TelegramOrganizationNotifications:Enabled"),
            isClientConnected = client.TelegramChatId.HasValue && client.TelegramNotificationsEnabled,
            botUsername = bot?.Username,
            recentNotifications
        };
    }

    /// <summary> Возвращает данные Telegram-уведомлений по обслуживанию. </summary>
    public async Task<object> GetMaintenanceNotificationsAsync(string userId, Guid organizationId, Guid maintenanceId)
    {
        await subscriptionService.EnsureWorkspaceCanReadAsync(userId, organizationId, OrganizationPermissionCodes.MaintenanceView);
        var canReadReminderBody = permissionService != null
            && await permissionService.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.ClientsView)
            && await permissionService.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.VehiclesView);
        if (!await (from maintenance in context.Maintenance.AsNoTracking()
            join vehicle in context.Vehicles.AsNoTracking() on maintenance.VehicleId equals vehicle.Id
            where maintenance.Id == maintenanceId && vehicle.OrganizationId == organizationId
            select maintenance.Id).AnyAsync())
        {
            throw new KeyNotFoundException("Обслуживание не найдено.");
        }

        return await context.TelegramClientNotifications.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.MaintenanceId == maintenanceId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new { x.Id, x.EventCode, Body = x.EventCode == MaintenanceVisitReminderRules.EventCode && !canReadReminderBody
                ? "Текст недоступен без прав просмотра клиентов и автомобилей." : x.Body, x.Status, x.CreatedAt, x.SentAt, x.ErrorCode })
            .ToListAsync();
    }

    /// <summary> Создаёт ссылку привязки клиента к боту Telegram. </summary>
    public async Task<string> CreateClientLinkAsync(string userId, Guid organizationId, Guid clientId)
    {
        await subscriptionService.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.ClientsEdit);
        await this.EnsureClientMessagingAccessAsync(userId, organizationId);
        var bot = await context.OrganizationTelegramBots.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.IsEnabled)
            ?? throw new InvalidOperationException("Сначала подключите бота Telegram в настройках организации.");
        var client = await context.OrganizationClients
            .SingleOrDefaultAsync(x => x.Id == clientId && x.OrganizationId == organizationId && !x.IsDeleted)
            ?? throw new KeyNotFoundException("Клиент не найден.");

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        client.TelegramLinkTokenHash = HashToken(token);
        client.TelegramLinkExpiresAt = DateTime.UtcNow.AddDays(7);
        await context.SaveChangesAsync();

        return $"https://t.me/{bot.Username}?start={token}";
    }

    /// <summary> Отвязывает клиента от бота Telegram. </summary>
    public async Task DisconnectClientAsync(string userId, Guid organizationId, Guid clientId)
    {
        await subscriptionService.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.ClientsEdit);
        var executionStrategy = context.Database.CreateExecutionStrategy();
        await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            try
            {
                await channelService.RevokeClientAsync(organizationId, clientId, "telegram", DateTime.UtcNow);
                var client = await context.OrganizationClients
                    .SingleOrDefaultAsync(x => x.Id == clientId && x.OrganizationId == organizationId && !x.IsDeleted)
                    ?? throw new KeyNotFoundException("Клиент не найден.");
                client.TelegramChatId = null;
                client.TelegramConnectedAt = null;
                client.TelegramNotificationsEnabled = false;
                client.TelegramLinkTokenHash = null;
                client.TelegramLinkExpiresAt = null;
                await context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                context.ChangeTracker.Clear();
                throw;
            }
        });
    }

    /// <summary> Обрабатывает входящее обновление webhook Telegram. </summary>
    public async Task<bool> ProcessWebhookAsync(
        Guid organizationId,
        string? suppliedSecret,
        JsonElement update,
        CancellationToken cancellationToken = default)
    {
        var bot = await context.OrganizationTelegramBots.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrganizationId == organizationId);
        if (bot == null || string.IsNullOrEmpty(suppliedSecret))
        {
            return false;
        }

        var expectedSecret = this._protector.Unprotect(bot.ProtectedWebhookSecret);
        if (!CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expectedSecret), Encoding.UTF8.GetBytes(suppliedSecret)))
        {
            return false;
        }

        if (!await this.HasClientMessagingAccessAsync(organizationId, cancellationToken))
        {
            return true;
        }

        if (!bot.IsEnabled)
        {
            return true;
        }

        if (update.ValueKind != JsonValueKind.Object)
        {
            return true;
        }

        if (update.TryGetProperty("message", out var message)
            && message.ValueKind == JsonValueKind.Object
            && message.TryGetProperty("chat", out var chat)
            && chat.ValueKind == JsonValueKind.Object
            && chat.TryGetProperty("type", out var chatType)
            && chatType.ValueKind == JsonValueKind.String
            && chatType.GetString() == "private"
            && chat.TryGetProperty("id", out var chatIdProperty)
            && chatIdProperty.ValueKind == JsonValueKind.Number
            && chatIdProperty.TryGetInt64(out var chatId)
            && chatId > 0)
        {
            var text = GetMessageText(message);
            var messageSentAt = GetMessageSentAt(message);
            var isStartCommand = text?.StartsWith("/start", StringComparison.Ordinal) == true;
            if (isStartCommand && text!.StartsWith("/start ", StringComparison.Ordinal))
            {
                var token = text[7..].Trim();
                var hash = HashToken(token);
                var client = await context.OrganizationClients.SingleOrDefaultAsync(x =>
                    x.OrganizationId == organizationId && x.TelegramLinkTokenHash == hash
                    && x.TelegramLinkExpiresAt > DateTime.UtcNow && !x.IsDeleted, cancellationToken);
                if (client != null && messageSentAt.HasValue)
                {
                    if (bot.TelegramBotId is null)
                    {
                        bot = await this.EnsureTelegramBotIdentityAsync(organizationId, bot, cancellationToken);
                    }

                    if (bot.TelegramBotId is > 0)
                    {
                        var executionStrategy = context.Database.CreateExecutionStrategy();
                        await executionStrategy.ExecuteAsync(async () =>
                        {
                            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
                            var isBound = await conversationService.BindTelegramClientAsync(
                                organizationId,
                                bot.TelegramBotId.Value,
                                chatId,
                                client.Id,
                                messageSentAt.Value,
                                cancellationToken);
                            if (!isBound)
                            {
                                await transaction.RollbackAsync(cancellationToken);
                                return;
                            }

                            var previousClients = await context.OrganizationClients
                                .Where(x => x.OrganizationId == organizationId
                                    && x.TelegramChatId == chatId && x.Id != client.Id)
                                .ToListAsync(cancellationToken);
                            foreach (var previousClient in previousClients)
                            {
                                previousClient.TelegramChatId = null;
                                previousClient.TelegramConnectedAt = null;
                                previousClient.TelegramNotificationsEnabled = false;
                            }

                            client.TelegramChatId = chatId;
                            client.TelegramConnectedAt = messageSentAt.Value;
                            client.TelegramNotificationsEnabled = true;
                            client.TelegramLinkTokenHash = null;
                            client.TelegramLinkExpiresAt = null;
                            await context.SaveChangesAsync(cancellationToken);
                            await transaction.CommitAsync(cancellationToken);
                        });
                    }

                }
            }

            if (!isStartCommand
                && TryCreateChatUpdate(organizationId, bot, update, message, chatId, out var chatUpdate))
            {
                if (bot.TelegramBotId is null)
                {
                    bot = await this.EnsureTelegramBotIdentityAsync(organizationId, bot, cancellationToken);
                    chatUpdate = chatUpdate with { BotId = bot.TelegramBotId ?? 0, BotUsername = bot.Username };
                }

                if (bot.TelegramBotId is > 0)
                {
                    await conversationService.IngestTelegramMessageAsync(chatUpdate, cancellationToken);
                }
            }
        }

        if (update.TryGetProperty("my_chat_member", out var member)
            && member.ValueKind == JsonValueKind.Object
            && member.TryGetProperty("chat", out var memberChat)
            && memberChat.ValueKind == JsonValueKind.Object
            && memberChat.TryGetProperty("type", out var memberChatType)
            && memberChatType.ValueKind == JsonValueKind.String
            && memberChatType.GetString() == "private"
            && memberChat.TryGetProperty("id", out var memberChatId)
            && memberChatId.ValueKind == JsonValueKind.Number
            && memberChatId.TryGetInt64(out var blockedChatId)
            && member.TryGetProperty("new_chat_member", out var newMember)
            && newMember.ValueKind == JsonValueKind.Object
            && newMember.TryGetProperty("status", out var status)
            && status.ValueKind == JsonValueKind.String
            && status.GetString() is "kicked" or "left")
        {
            var eventAt = member.TryGetProperty("date", out var dateProperty)
                && dateProperty.ValueKind == JsonValueKind.Number
                && dateProperty.TryGetInt64(out var timestamp)
                && timestamp > 0 && timestamp <= DateTimeOffset.MaxValue.ToUnixTimeSeconds()
                ? DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime
                : DateTime.UtcNow;
            if (bot.TelegramBotId is null)
            {
                bot = await this.EnsureTelegramBotIdentityAsync(organizationId, bot, cancellationToken);
            }

            if (bot.TelegramBotId is > 0)
            {
                await conversationService.SetTelegramChatActiveAsync(
                    organizationId,
                    bot.TelegramBotId.Value,
                    blockedChatId,
                    false,
                    eventAt,
                    cancellationToken);
            }

            var clients = await context.OrganizationClients
                .Where(x => x.OrganizationId == organizationId && x.TelegramChatId == blockedChatId
                    && x.TelegramConnectedAt <= eventAt)
                .ToListAsync(cancellationToken);
            foreach (var client in clients)
            {
                client.TelegramNotificationsEnabled = false;
            }

            await context.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    /// <summary> Проверяет, что пользователь — владелец организации. </summary>
    private async Task EnsureOwnerAsync(string userId, Guid organizationId)
    {
        await subscriptionService.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.WorkspaceProfileEdit);
        if (!await context.Organizations.AnyAsync(x => x.Id == organizationId && x.OwnerUserId == userId && !x.IsDeleted))
        {
            throw new UnauthorizedAccessException("Настроить бота Telegram может только владелец организации.");
        }
    }

    private async Task<bool> HasClientMessagingAccessAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var ownerUserId = await context.Organizations.AsNoTracking()
            .Where(item => item.Id == organizationId && item.IsActive && !item.IsDeleted && !item.IsArchived)
            .Select(item => item.OwnerUserId)
            .SingleOrDefaultAsync(cancellationToken);

        return ownerUserId != null
            && await subscriptionService.HasFeatureAsync(ownerUserId, organizationId, Helpers.FeatureCodes.ClientMessaging);
    }

    private async Task EnsureClientMessagingAccessAsync(string userId, Guid organizationId)
    {
        if (!await subscriptionService.HasFeatureAsync(userId, organizationId, Helpers.FeatureCodes.ClientMessaging))
        {
            throw new InvalidOperationException("Боты и сообщения клиентам доступны по подписке PRO.");
        }
    }

    /// <summary> Формирует URL webhook для организации. </summary>
    private string GetWebhookUrl(Guid organizationId)
    {
        var baseUrl = configuration["TelegramOrganizationNotifications:RelayBaseUrl"];
        baseUrl = baseUrl?.TrimEnd('/');
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return string.Empty;
        }

        return $"{baseUrl}/updates/{organizationId:D}";
    }

    /// <summary> Получает подтверждённые Telegram имя и неизменяемый идентификатор бота. </summary>
    private async Task<OrganizationTelegramIdentity> GetBotIdentityAsync(string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "getOrganizationBot")
        {
            Content = JsonContent.Create(new { bot_token = token })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration["TelegramOrganizationNotifications:RelayKey"]);
        using var response = await httpClientFactory.CreateClient("TelegramOrganizationBot").SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("Telegram не принял токен бота.");
        }

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var responseRoot = document.RootElement;
        if (!responseRoot.TryGetProperty("ok", out var accepted) || accepted.ValueKind != JsonValueKind.True
            || !responseRoot.TryGetProperty("result", out var root) || root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("is_bot", out var isBot) || isBot.ValueKind != JsonValueKind.True
            || !root.TryGetProperty("id", out var idProperty)
            || idProperty.ValueKind != JsonValueKind.Number
            || !idProperty.TryGetInt64(out var botId)
            || botId <= 0
            || !root.TryGetProperty("username", out var usernameProperty)
            || string.IsNullOrWhiteSpace(usernameProperty.GetString()))
        {
            throw new InvalidOperationException("Telegram не вернул имя бота.");
        }

        return new OrganizationTelegramIdentity(botId, usernameProperty.GetString()!.TrimStart('@').ToLowerInvariant());
    }

    private async Task<OrganizationTelegramBot> EnsureTelegramBotIdentityAsync(
        Guid organizationId,
        OrganizationTelegramBot webhookBot,
        CancellationToken cancellationToken)
    {
        var identity = await this.GetBotIdentityAsync(
            this._protector.Unprotect(webhookBot.ProtectedToken), cancellationToken);
        var bot = await context.OrganizationTelegramBots.SingleAsync(
            item => item.OrganizationId == organizationId, cancellationToken);
        if (bot.TelegramBotId == null)
        {
            bot.TelegramBotId = identity.Id;
            bot.Username = identity.Username;
            bot.UpdatedAt = DateTime.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
        }
        else if (bot.TelegramBotId != identity.Id)
        {
            throw new InvalidOperationException("Идентификатор подключённого Telegram-бота изменился.");
        }

        return bot;
    }

    private static bool TryCreateChatUpdate(
        Guid organizationId,
        OrganizationTelegramBot bot,
        JsonElement update,
        JsonElement message,
        long chatId,
        out IngestTelegramMessageDto chatUpdate)
    {
        chatUpdate = new IngestTelegramMessageDto();
        if (!update.TryGetProperty("update_id", out var updateIdProperty)
            || updateIdProperty.ValueKind != JsonValueKind.Number
            || !updateIdProperty.TryGetInt64(out var updateId)
            || updateId < 0
            || !message.TryGetProperty("message_id", out var messageIdProperty)
            || messageIdProperty.ValueKind != JsonValueKind.Number
            || !messageIdProperty.TryGetInt64(out var messageId)
            || messageId <= 0
            || !message.TryGetProperty("from", out var sender)
            || sender.ValueKind != JsonValueKind.Object
            || !sender.TryGetProperty("id", out var senderIdProperty)
            || senderIdProperty.ValueKind != JsonValueKind.Number
            || !senderIdProperty.TryGetInt64(out var senderId)
            || senderId <= 0
            || !message.TryGetProperty("date", out var dateProperty)
            || dateProperty.ValueKind != JsonValueKind.Number
            || !dateProperty.TryGetInt64(out var unixTime)
            || unixTime < 0
            || unixTime > DateTimeOffset.MaxValue.ToUnixTimeSeconds())
        {
            return false;
        }

        var text = GetMessageText(message);
        var attachments = GetAttachments(message);

        var senderName = string.Join(" ", new[]
        {
            GetString(sender, "first_name"),
            GetString(sender, "last_name")
        }.Where(value => !string.IsNullOrWhiteSpace(value)));

        chatUpdate = new IngestTelegramMessageDto
        {
            OrganizationId = organizationId,
            BotId = bot.TelegramBotId ?? 0,
            BotUsername = bot.Username,
            UpdateId = updateId,
            ChatId = chatId,
            ProviderMessageId = messageId,
            SenderTelegramUserId = senderId,
            SenderDisplayName = string.IsNullOrWhiteSpace(senderName) ? null : senderName,
            SenderUsername = GetString(sender, "username"),
            Text = string.IsNullOrWhiteSpace(text) ? null : text,
            SentAt = DateTimeOffset.FromUnixTimeSeconds(unixTime).UtcDateTime,
            Attachments = attachments
        };

        return true;
    }

    private static string? GetMessageText(JsonElement message)
    {
        var text = GetString(message, "text") ?? GetString(message, "caption");
        if (!string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        if (message.TryGetProperty("voice", out _))
        {
            return "[Голосовое сообщение: просмотр этого формата пока недоступен]";
        }

        if (message.TryGetProperty("video", out _))
        {
            return "[Видео: просмотр этого формата пока недоступен]";
        }

        if (message.TryGetProperty("video_note", out _))
        {
            return "[Видеосообщение: просмотр этого формата пока недоступен]";
        }

        if (message.TryGetProperty("sticker", out _))
        {
            return "[Стикер: просмотр этого формата пока недоступен]";
        }

        if (message.TryGetProperty("audio", out _))
        {
            return "[Аудиофайл: просмотр этого формата пока недоступен]";
        }

        if (message.TryGetProperty("animation", out _))
        {
            return "[GIF-анимация: просмотр этого формата пока недоступен]";
        }

        if (message.TryGetProperty("location", out _))
        {
            return "[Геопозиция: просмотр этого формата пока недоступен]";
        }

        if (message.TryGetProperty("contact", out _))
        {
            return "[Контакт: просмотр этого формата пока недоступен]";
        }

        if (message.TryGetProperty("venue", out _))
        {
            return "[Место: просмотр этого формата пока недоступен]";
        }

        if (message.TryGetProperty("poll", out _))
        {
            return "[Опрос: просмотр этого формата пока недоступен]";
        }

        return "[Сообщение Telegram: этот формат пока недоступен]";
    }

    private static DateTime? GetMessageSentAt(JsonElement message)
    {
        if (!message.TryGetProperty("date", out var dateProperty)
            || dateProperty.ValueKind != JsonValueKind.Number
            || !dateProperty.TryGetInt64(out var unixTime)
            || unixTime < 0
            || unixTime > DateTimeOffset.MaxValue.ToUnixTimeSeconds())
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeSeconds(unixTime).UtcDateTime;
    }

    private static List<IngestTelegramAttachmentDto> GetAttachments(JsonElement message)
    {
        var attachments = new List<IngestTelegramAttachmentDto>();
        if (message.TryGetProperty("photo", out var photoSizes)
            && photoSizes.ValueKind == JsonValueKind.Array
            && photoSizes.GetArrayLength() > 0)
        {
            var photo = photoSizes[photoSizes.GetArrayLength() - 1];
            var photoFileId = GetString(photo, "file_id");
            if (!string.IsNullOrWhiteSpace(photoFileId))
            {
                attachments.Add(new IngestTelegramAttachmentDto
                {
                    Kind = "photo",
                    FileName = "photo.jpg",
                    ContentType = "image/jpeg",
                    SizeBytes = GetLong(photo, "file_size") ?? 0,
                    ProviderFileId = photoFileId,
                    ProviderFileUniqueId = GetString(photo, "file_unique_id")
                });
            }
        }

        if (message.TryGetProperty("document", out var document)
            && document.ValueKind == JsonValueKind.Object)
        {
            var fileId = GetString(document, "file_id");
            if (!string.IsNullOrWhiteSpace(fileId))
            {
                attachments.Add(new IngestTelegramAttachmentDto
                {
                    Kind = "document",
                    FileName = GetString(document, "file_name") ?? "document",
                    ContentType = GetString(document, "mime_type") ?? "application/octet-stream",
                    SizeBytes = GetLong(document, "file_size") ?? 0,
                    ProviderFileId = fileId,
                    ProviderFileUniqueId = GetString(document, "file_unique_id")
                });
            }
        }

        return attachments;
    }

    private static string? GetString(JsonElement parent, string name)
    {
        return parent.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static long? GetLong(JsonElement parent, string name)
    {
        return parent.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt64(out var value)
                ? value
                : null;
    }

    private sealed record OrganizationTelegramIdentity(long Id, string Username);

    /// <summary> Регистрирует webhook для бота Telegram организации. </summary>
    private async Task RegisterWebhookAsync(string token, Guid organizationId, string secret, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "setOrganizationWebhook")
        {
            Content = JsonContent.Create(new { bot_token = token, organization_id = organizationId.ToString("D"), secret })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration["TelegramOrganizationNotifications:RelayKey"]);
        using var response = await httpClientFactory.CreateClient("TelegramOrganizationBot").SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("Telegram не принял адрес вебхука. Проверьте доступность HTTPS-сайта.");
        }

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("ok", out var success) || success.ValueKind != JsonValueKind.True)
        {
            throw new InvalidOperationException("Telegram не подтвердил подписку на события бота.");
        }
    }

    /// <summary> Вычисляет хеш токена бота для хранения. </summary>
    private static string HashToken(string token)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
