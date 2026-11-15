using System.Globalization;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Communications.Contracts;
using OpenCRM.Modules.Communications.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Notifications.Max;

/// <summary> Сервис настройки и обработки уведомлений Max для организации. </summary>
public sealed class OrganizationMaxService(
    OpenCRMContext context,
    IDataProtectionProvider dataProtectionProvider,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    IOrganizationSubscriptionService subscriptionService,
    IOrganizationChannelService channelService,
    IOrganizationPermissionService? permissionService = null)
{
    private sealed record MaxBotIdentity(string ProviderBotId, string Username);

    /// <summary> Защититель данных для шифрования токена бота. </summary>
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector("OpenCRM.OrganizationMaxBot.v1");

    /// <summary> Возвращает учётные данные бота Max для уведомления организации. </summary>
    public async Task<string?> GetBotCredentialAsync(
        Guid organizationId,
        string notificationKey,
        CancellationToken cancellationToken)
    {
        if (!await this.HasClientMessagingAccessAsync(organizationId, cancellationToken))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(notificationKey))
        {
            return null;
        }

        var notification = await context.MaxClientNotifications.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrganizationId == organizationId
                && x.NotificationKey == notificationKey, cancellationToken);
        if (notification == null || notification.Status is "failed" or "skipped" or "sent" or "simulated")
        {
            return null;
        }

        var client = await context.OrganizationClients.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == notification.ClientId && x.OrganizationId == organizationId, cancellationToken);
        if (client == null || client.IsDeleted || !client.MaxNotificationsEnabled
            || client.MaxUserId != notification.MaxUserId)
        {
            return null;
        }

        var maintenance = await context.Maintenance.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == notification.MaintenanceId, cancellationToken);
        var vehicle = maintenance == null ? null : await context.Vehicles.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == maintenance.VehicleId, cancellationToken);
        if (maintenance == null || vehicle?.OrganizationId != organizationId
            || vehicle.OrganizationClientId != notification.ClientId
            || await context.MaxClientNotifications.AsNoTracking().AnyAsync(x => x.MaintenanceId == notification.MaintenanceId
                && x.ClientId == notification.ClientId && x.Id > notification.Id
                && x.EventCode != MaintenanceVisitReminderRules.EventCode, cancellationToken)
            || notification.EventCode == MaintenanceVisitReminderRules.EventCode
                && !await VisitReminderDeliveryGuard.IsCurrentAsync(context, configuration, maintenance, organizationId, client,
                    notification.VisitStartsAtUtc, notification.NotificationKey, "max", cancellationToken)
            || notification.EventCode == "ready" && (!maintenance.ReadyForHandoverAt.HasValue || maintenance.HandedOverAt.HasValue)
            || notification.EventCode == "booking_cancelled" && maintenance.Status != OpenCRM.Domain.Enums.MaintenanceStatus.Cancelled
            || notification.EventCode is "booking_created" or "booking_changed"
                && maintenance.Status is not (OpenCRM.Domain.Enums.MaintenanceStatus.Planned
                    or OpenCRM.Domain.Enums.MaintenanceStatus.InProgress))
        {
            return null;
        }

        var bot = await (from configuredBot in context.OrganizationMaxBots.AsNoTracking()
            join organization in context.Organizations.AsNoTracking()
                on configuredBot.OrganizationId equals organization.Id
            where configuredBot.OrganizationId == organizationId && configuredBot.IsEnabled
                && (notification.EventCode == "ready" ? configuredBot.NotifyReady : configuredBot.NotifyBooking)
                && organization.IsActive && !organization.IsDeleted && !organization.IsArchived
            select configuredBot.ProtectedToken).SingleOrDefaultAsync(cancellationToken);

        return bot == null ? null : this._protector.Unprotect(bot);
    }

    /// <summary> Возвращает настройки уведомлений Max организации. </summary>
    public async Task<object> GetSettingsAsync(string userId, Guid organizationId)
    {
        await this.EnsureOwnerAsync(userId, organizationId);
        var bot = await context.OrganizationMaxBots.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrganizationId == organizationId);

        return new
        {
            botUsername = bot?.Username,
            isConfigured = bot != null,
            isEnabled = bot?.IsEnabled ?? false,
            notifyBooking = bot?.NotifyBooking ?? true,
            notifyReady = bot?.NotifyReady ?? true,
            webhookUrl = this.GetWebhookUrl(organizationId)
        };
    }

    /// <summary> Сохраняет настройки уведомлений Max организации. </summary>
    public async Task SaveSettingsAsync(string userId, Guid organizationId, string? token, bool isEnabled, bool notifyBooking, bool notifyReady, CancellationToken cancellationToken)
    {
        await this.EnsureOwnerAsync(userId, organizationId);
        if (isEnabled || !string.IsNullOrWhiteSpace(token))
        {
            await this.EnsureClientMessagingAccessAsync(userId, organizationId);
        }

        var bot = await context.OrganizationMaxBots.SingleOrDefaultAsync(x => x.OrganizationId == organizationId, cancellationToken);
        token = token?.Trim();
        if (bot == null && string.IsNullOrEmpty(token))
        {
            throw new InvalidOperationException("Укажите токен бота MAX.");
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
        var webhookUrl = this.GetWebhookUrl(organizationId);
        try
        {
            var identity = await this.GetBotIdentityAsync(effectiveToken, cancellationToken);
            if (await context.OrganizationMaxBots.AnyAsync(x =>
                x.OrganizationId != organizationId
                    && (x.ImmutableProviderBotId == identity.ProviderBotId
                        || x.ImmutableProviderBotId == null && x.Username == identity.Username),
                cancellationToken))
            {
                throw new InvalidOperationException("Этот бот MAX уже подключён к другой организации.");
            }
            var secret = bot == null || !string.IsNullOrEmpty(token)
                ? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_')
                : this._protector.Unprotect(bot.ProtectedWebhookSecret);
            await this.RegisterWebhookAsync(effectiveToken, webhookUrl, secret, cancellationToken);

            if (bot == null)
            {
                bot = new OrganizationMaxBot
                {
                    OrganizationId = organizationId,
                    ProtectedWebhookSecret = this._protector.Protect(secret)
                };
                context.OrganizationMaxBots.Add(bot);
            }

            if (bot.ImmutableProviderBotId != identity.ProviderBotId)
            {
                var clients = await context.OrganizationClients
                    .Where(x => x.OrganizationId == organizationId && x.MaxUserId != null)
                    .ToListAsync(cancellationToken);
                foreach (var client in clients)
                {
                    client.MaxUserId = null;
                    client.MaxConnectedAt = null;
                    client.MaxNotificationsEnabled = false;
                }
            }

            bot.Username = identity.Username;
            bot.ImmutableProviderBotId = identity.ProviderBotId;
            bot.ProtectedToken = this._protector.Protect(effectiveToken);
            bot.ProtectedWebhookSecret = this._protector.Protect(secret);
            bot.IsEnabled = isEnabled;
            bot.NotifyBooking = notifyBooking;
            bot.NotifyReady = notifyReady;
            bot.UpdatedAt = DateTime.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
            await channelService.EnsureConnectionAsync(new EnsureOrganizationChannelConnectionDto
            {
                OrganizationId = organizationId,
                ProviderCode = "max",
                ImmutableProviderIdentity = identity.ProviderBotId,
                Username = identity.Username,
                IsVerified = true,
                VerifiedAt = DateTime.UtcNow
            }, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("MAX не подтвердил подключение бота. Проверьте токен и повторите попытку.");
        }
    }

    /// <summary> Возвращает статус привязки клиента к Max. </summary>
    public async Task<object> GetClientStatusAsync(string userId, Guid organizationId, Guid clientId)
    {
        await subscriptionService.EnsureWorkspaceCanReadAsync(userId, organizationId, OrganizationPermissionCodes.ClientsView);
        var client = await context.OrganizationClients.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == clientId && x.OrganizationId == organizationId && !x.IsDeleted)
            ?? throw new KeyNotFoundException("Клиент не найден.");
        var bot = await context.OrganizationMaxBots.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrganizationId == organizationId);
        var recentNotifications = await context.MaxClientNotifications.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.ClientId == clientId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new { x.Id, x.EventCode, x.Body, x.Status, x.CreatedAt, x.SentAt, x.ErrorCode })
            .ToListAsync();

        return new
        {
            isBotConnected = bot?.IsEnabled ?? false,
            isClientConnected = client.MaxUserId.HasValue && client.MaxNotificationsEnabled,
            botUsername = bot?.Username,
            recentNotifications
        };
    }

    /// <summary> Возвращает данные уведомлений Max по обслуживанию. </summary>
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

        return await context.MaxClientNotifications.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.MaintenanceId == maintenanceId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new { x.Id, x.EventCode, Body = x.EventCode == MaintenanceVisitReminderRules.EventCode && !canReadReminderBody
                ? "Текст недоступен без прав просмотра клиентов и автомобилей." : x.Body, x.Status, x.CreatedAt, x.SentAt, x.ErrorCode })
            .ToListAsync();
    }

    /// <summary> Создаёт ссылку привязки клиента к боту Max. </summary>
    public async Task<string> CreateClientLinkAsync(string userId, Guid organizationId, Guid clientId)
    {
        await subscriptionService.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.ClientsEdit);
        await this.EnsureClientMessagingAccessAsync(userId, organizationId);
        var bot = await context.OrganizationMaxBots.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.IsEnabled)
            ?? throw new InvalidOperationException("Сначала подключите бота MAX в настройках организации.");
        var client = await context.OrganizationClients
            .SingleOrDefaultAsync(x => x.Id == clientId && x.OrganizationId == organizationId && !x.IsDeleted)
            ?? throw new KeyNotFoundException("Клиент не найден.");

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        client.MaxLinkTokenHash = HashToken(token);
        client.MaxLinkExpiresAt = DateTime.UtcNow.AddDays(7);
        await context.SaveChangesAsync();

        return $"https://max.ru/{bot.Username}?start={token}";
    }

    /// <summary> Отвязывает клиента от бота Max. </summary>
    public async Task DisconnectClientAsync(string userId, Guid organizationId, Guid clientId)
    {
        await subscriptionService.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.ClientsEdit);
        await this.ExecuteInTransactionAsync(async () =>
        {
            await channelService.RevokeClientAsync(organizationId, clientId, "max", DateTime.UtcNow);
            var client = await context.OrganizationClients
                .SingleOrDefaultAsync(x => x.Id == clientId && x.OrganizationId == organizationId && !x.IsDeleted)
                ?? throw new KeyNotFoundException("Клиент не найден.");
            client.MaxUserId = null;
            client.MaxConnectedAt = null;
            client.MaxNotificationsEnabled = false;
            client.MaxLinkTokenHash = null;
            client.MaxLinkExpiresAt = null;

            return true;
        }, CancellationToken.None);
    }

    /// <summary> Обрабатывает входящее обновление webhook MAX. </summary>
    public async Task<bool> ProcessWebhookAsync(
        Guid organizationId,
        string? suppliedSecret,
        JsonElement update,
        CancellationToken cancellationToken = default)
    {
        var bot = await context.OrganizationMaxBots
            .SingleOrDefaultAsync(x => x.OrganizationId == organizationId, cancellationToken);
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

        if (update.ValueKind != JsonValueKind.Object
            || !update.TryGetProperty("update_type", out var typeProperty)
            || typeProperty.ValueKind != JsonValueKind.String)
        {
            return true;
        }

        var type = typeProperty.GetString();
        if (type == "message_created")
        {
            if (bot.IsEnabled)
            {
                await this.EnsureVerifiedBotIdentityAsync(organizationId, bot, cancellationToken);
                if (!string.IsNullOrWhiteSpace(bot.ImmutableProviderBotId))
                {
                    await this.IngestPrivateMessageAsync(organizationId, bot, update, cancellationToken);
                }
            }

            return true;
        }

        if (!update.TryGetProperty("user", out var userProperty)
            || userProperty.ValueKind != JsonValueKind.Object
            || !TryReadInt64(userProperty, "user_id", out var maxUserId))
        {
            return true;
        }

        if (type == "bot_started")
        {
            if (update.TryGetProperty("payload", out var payloadProperty)
                && payloadProperty.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(payloadProperty.GetString()))
            {
                await this.BindClientFromStartAsync(
                    organizationId,
                    bot,
                    maxUserId,
                    payloadProperty.GetString()!,
                    update,
                    cancellationToken);
            }
            else if (!update.TryGetProperty("payload", out payloadProperty)
                || payloadProperty.ValueKind is JsonValueKind.Null or JsonValueKind.String)
            {
                await this.ResumeExistingClientAsync(organizationId, bot, maxUserId, update, cancellationToken);
            }
        }
        else if (type == "bot_stopped")
        {
            await this.DeactivateClientChatAsync(organizationId, maxUserId, update, cancellationToken);
        }

        return true;
    }

    private async Task EnsureVerifiedBotIdentityAsync(
        Guid organizationId,
        OrganizationMaxBot bot,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(bot.ImmutableProviderBotId))
        {
            return;
        }

        var identity = await this.GetBotIdentityAsync(this._protector.Unprotect(bot.ProtectedToken), cancellationToken);
        bot.ImmutableProviderBotId = identity.ProviderBotId;
        bot.Username = identity.Username;
        await context.SaveChangesAsync(cancellationToken);
        await channelService.EnsureConnectionAsync(new EnsureOrganizationChannelConnectionDto
        {
            OrganizationId = organizationId,
            ProviderCode = "max",
            ImmutableProviderIdentity = identity.ProviderBotId,
            Username = identity.Username,
            IsVerified = true,
            VerifiedAt = DateTime.UtcNow
        }, cancellationToken);
    }

    private async Task BindClientFromStartAsync(
        Guid organizationId,
        OrganizationMaxBot bot,
        long maxUserId,
        string token,
        JsonElement update,
        CancellationToken cancellationToken)
    {
        if (!bot.IsEnabled)
        {
            return;
        }

        var tokenHash = HashToken(token);
        if (!await context.OrganizationClients.AsNoTracking().AnyAsync(x =>
            x.OrganizationId == organizationId && x.MaxLinkTokenHash == tokenHash
            && x.MaxLinkExpiresAt > DateTime.UtcNow && !x.IsDeleted,
            cancellationToken))
        {
            return;
        }

        await this.EnsureVerifiedBotIdentityAsync(organizationId, bot, cancellationToken);
        var providerBotId = bot.ImmutableProviderBotId;
        if (string.IsNullOrWhiteSpace(providerBotId))
        {
            return;
        }

        var connectionId = await channelService.EnsureConnectionAsync(new EnsureOrganizationChannelConnectionDto
        {
            OrganizationId = organizationId,
            ProviderCode = "max",
            ImmutableProviderIdentity = providerBotId,
            Username = bot.Username,
            IsVerified = true,
            VerifiedAt = DateTime.UtcNow
        }, cancellationToken);
        var occurredAt = ReadOccurredAt(update) ?? DateTime.UtcNow;
        var externalConversationId = ReadStringId(update, "chat_id")
            ?? maxUserId.ToString(CultureInfo.InvariantCulture);
        await this.ExecuteInTransactionAsync(async () =>
        {
            var client = await context.OrganizationClients.SingleOrDefaultAsync(x =>
                x.OrganizationId == organizationId && x.MaxLinkTokenHash == tokenHash
                && x.MaxLinkExpiresAt > DateTime.UtcNow && !x.IsDeleted,
                cancellationToken);
            if (client == null
                || !await channelService.BindClientAsync(
                    organizationId,
                    connectionId,
                    externalConversationId,
                    maxUserId.ToString(CultureInfo.InvariantCulture),
                    client.Id,
                    occurredAt,
                    cancellationToken))
            {
                return false;
            }

            var previousClients = await context.OrganizationClients
                .Where(item => item.OrganizationId == organizationId && item.MaxUserId == maxUserId && item.Id != client.Id)
                .ToListAsync(cancellationToken);
            foreach (var previousClient in previousClients)
            {
                previousClient.MaxUserId = null;
                previousClient.MaxConnectedAt = null;
                previousClient.MaxNotificationsEnabled = false;
            }

            client.MaxUserId = maxUserId;
            client.MaxConnectedAt = occurredAt;
            client.MaxNotificationsEnabled = true;
            client.MaxLinkTokenHash = null;
            client.MaxLinkExpiresAt = null;

            return true;
        }, cancellationToken);
    }

    private async Task ResumeExistingClientAsync(
        Guid organizationId,
        OrganizationMaxBot bot,
        long maxUserId,
        JsonElement update,
        CancellationToken cancellationToken)
    {
        if (!bot.IsEnabled || string.IsNullOrWhiteSpace(bot.ImmutableProviderBotId))
        {
            return;
        }

        var occurredAt = ReadOccurredAt(update) ?? DateTime.UtcNow;
        var externalConversationId = ReadStringId(update, "chat_id")
            ?? maxUserId.ToString(CultureInfo.InvariantCulture);
        await this.ExecuteInTransactionAsync(async () =>
        {
            var client = await context.OrganizationClients.SingleOrDefaultAsync(x =>
                x.OrganizationId == organizationId && x.MaxUserId == maxUserId && !x.IsDeleted,
                cancellationToken);
            if (client == null)
            {
                return false;
            }

            var connection = await context.OrganizationChannelConnections.AsNoTracking()
                .SingleOrDefaultAsync(x => x.OrganizationId == organizationId
                    && x.ProviderCode == "max"
                    && x.ImmutableProviderIdentity == bot.ImmutableProviderBotId
                    && x.IsCurrent,
                    cancellationToken);
            if (connection == null
                || !await channelService.SetChannelActiveAsync(
                    organizationId,
                    connection.Id,
                    externalConversationId,
                    true,
                    occurredAt,
                    cancellationToken))
            {
                return false;
            }

            client.MaxNotificationsEnabled = true;

            return true;
        }, cancellationToken);
    }

    private async Task DeactivateClientChatAsync(
        Guid organizationId,
        long maxUserId,
        JsonElement update,
        CancellationToken cancellationToken)
    {
        var occurredAt = ReadOccurredAt(update) ?? DateTime.UtcNow;
        var externalConversationId = ReadStringId(update, "chat_id")
            ?? maxUserId.ToString(CultureInfo.InvariantCulture);
        await this.ExecuteInTransactionAsync(async () =>
        {
            var connection = await context.OrganizationChannelConnections.AsNoTracking()
                .SingleOrDefaultAsync(x => x.OrganizationId == organizationId
                    && x.ProviderCode == "max" && x.IsCurrent,
                    cancellationToken);
            if (connection != null
                && !await channelService.SetChannelActiveAsync(
                    organizationId,
                    connection.Id,
                    externalConversationId,
                    false,
                    occurredAt,
                    cancellationToken)
                && await context.OrganizationConversations.AnyAsync(conversation =>
                    conversation.OrganizationId == organizationId
                    && conversation.ConnectionId == connection.Id
                    && conversation.ExternalConversationId == externalConversationId,
                    cancellationToken))
            {
                return false;
            }

            var clients = await context.OrganizationClients
                .Where(x => x.OrganizationId == organizationId && x.MaxUserId == maxUserId
                    && x.MaxConnectedAt <= occurredAt)
                .ToListAsync(cancellationToken);
            foreach (var client in clients)
            {
                client.MaxNotificationsEnabled = false;
            }

            return clients.Count != 0;
        }, cancellationToken);
    }

    private async Task<T> ExecuteInTransactionAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        var executionStrategy = context.Database.CreateExecutionStrategy();

        return await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var result = await action();
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return result;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                context.ChangeTracker.Clear();
                throw;
            }
        });
    }

    /// <summary> Проверяет, что пользователь — владелец организации. </summary>
    private async Task EnsureOwnerAsync(string userId, Guid organizationId)
    {
        await subscriptionService.EnsureWorkspaceCanWriteAsync(userId, organizationId, OrganizationPermissionCodes.WorkspaceProfileEdit);
        if (!await context.Organizations.AnyAsync(x => x.Id == organizationId && x.OwnerUserId == userId && !x.IsDeleted))
        {
            throw new UnauthorizedAccessException("Настроить бота MAX может только владелец организации.");
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
        var baseUrl = configuration["AppSettings:BaseUrl"]?.TrimEnd('/');
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Для MAX нужен корректный HTTPS AppSettings:BaseUrl.");
        }

        return $"{baseUrl}/api/webhooks/max/{organizationId}";
    }

    /// <summary> Проверяет токен бота MAX и получает его неизменяемый идентификатор. </summary>
    private async Task<MaxBotIdentity> GetBotIdentityAsync(string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "me");
        request.Headers.TryAddWithoutValidation("Authorization", token);
        using var response = await httpClientFactory.CreateClient("MaxBot").SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("MAX не принял токен бота.");
        }

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var root = document.RootElement;
        if (!root.TryGetProperty("is_bot", out var isBot) || isBot.ValueKind != JsonValueKind.True
            || !TryReadInt64(root, "user_id", out var userId) || userId <= 0
            || !root.TryGetProperty("username", out var usernameProperty)
            || string.IsNullOrWhiteSpace(usernameProperty.GetString()))
        {
            throw new InvalidOperationException("MAX не вернул имя бота.");
        }

        return new MaxBotIdentity(
            userId.ToString(CultureInfo.InvariantCulture),
            usernameProperty.GetString()!.TrimStart('@').ToLowerInvariant());
    }

    /// <summary> Регистрирует webhook для бота Max. </summary>
    private async Task RegisterWebhookAsync(string token, string url, string secret, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "subscriptions")
        {
            Content = JsonContent.Create(new { url, update_types = new[] { "bot_started", "bot_stopped", "message_created" }, secret })
        };
        request.Headers.TryAddWithoutValidation("Authorization", token);
        using var response = await httpClientFactory.CreateClient("MaxBot").SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("MAX не принял адрес вебхука. Проверьте доступность HTTPS-сайта.");
        }

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True)
        {
            throw new InvalidOperationException("MAX не подтвердил подписку на события бота.");
        }
    }

    /// <summary> Вычисляет хеш токена бота для хранения. </summary>
    private static string HashToken(string token)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    private async Task IngestPrivateMessageAsync(
        Guid organizationId,
        OrganizationMaxBot bot,
        JsonElement update,
        CancellationToken cancellationToken)
    {
        if (!update.TryGetProperty("message", out var message)
            || message.ValueKind != JsonValueKind.Object
            || !message.TryGetProperty("sender", out var sender)
            || sender.ValueKind != JsonValueKind.Object
            || !TryReadInt64(sender, "user_id", out var senderId)
            || senderId <= 0
            || sender.TryGetProperty("is_bot", out var senderIsBot) && senderIsBot.ValueKind == JsonValueKind.True
            || !message.TryGetProperty("recipient", out var recipient)
            || recipient.ValueKind != JsonValueKind.Object
            || ReadNullableString(recipient, "chat_type") != "dialog")
        {
            return;
        }

        var chatId = ReadStringId(recipient, "chat_id")
            ?? ReadStringId(recipient, "user_id")
            ?? senderId.ToString(CultureInfo.InvariantCulture);
        if (!message.TryGetProperty("body", out var body) || body.ValueKind != JsonValueKind.Object)
        {
            return;
        }
        var providerMessageId = ReadStringId(body, "mid");
        if (string.IsNullOrWhiteSpace(chatId)
            || string.IsNullOrWhiteSpace(providerMessageId)
            || string.IsNullOrWhiteSpace(bot.ImmutableProviderBotId))
        {
            return;
        }

        var connectionId = await channelService.EnsureConnectionAsync(new EnsureOrganizationChannelConnectionDto
        {
            OrganizationId = organizationId,
            ProviderCode = "max",
            ImmutableProviderIdentity = bot.ImmutableProviderBotId,
            Username = bot.Username,
            IsVerified = true,
            VerifiedAt = DateTime.UtcNow
        }, cancellationToken);
        var text = ReadNullableString(body, "text");
        var attachments = ReadAttachments(body, providerMessageId);
        if (string.IsNullOrWhiteSpace(text) && attachments.Count == 0)
        {
            text = "Входящее сообщение MAX: формат сообщения пока не поддерживается.";
        }
        else if (string.IsNullOrWhiteSpace(text) && attachments.Any(attachment => attachment.ProviderFileId == null))
        {
            var unsupportedTypes = attachments
                .Where(attachment => attachment.ProviderFileId == null)
                .Select(attachment => attachment.Kind)
                .Where(kind => !string.IsNullOrWhiteSpace(kind))
                .Distinct(StringComparer.Ordinal);
            text = "Входящее сообщение MAX содержит вложение, которое пока нельзя просмотреть: "
                + string.Join(", ", unsupportedTypes) + ".";
        }

        var occurredAt = ReadOccurredAt(message) ?? ReadOccurredAt(update) ?? DateTime.UtcNow;
        await channelService.IngestMessageAsync(new ChannelMessageReceivedDto
        {
            OrganizationId = organizationId,
            ConnectionId = connectionId,
            ProviderCode = "max",
            ExternalConversationId = chatId,
            ExternalUserId = senderId.ToString(CultureInfo.InvariantCulture),
            DeliveryAddress = senderId.ToString(CultureInfo.InvariantCulture),
            ProviderUpdateId = ReadStringId(update, "update_id"),
            ProviderMessageId = providerMessageId,
            SenderDisplayName = ReadSenderName(sender),
            SenderUsername = ReadNullableString(sender, "username"),
            Text = text,
            Attachments = attachments,
            OccurredAt = occurredAt
        }, cancellationToken);
    }

    private static IReadOnlyList<ChannelAttachmentReceivedDto> ReadAttachments(JsonElement body, string providerMessageId)
    {
        if (body.ValueKind != JsonValueKind.Object
            || !body.TryGetProperty("attachments", out var value)
            || value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ChannelAttachmentReceivedDto>();
        }

        var attachments = new List<ChannelAttachmentReceivedDto>();
        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            var currentIndex = index++;
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("type", out var typeProperty)
                || typeProperty.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var kind = typeProperty.GetString() ?? string.Empty;
            attachments.Add(new ChannelAttachmentReceivedDto
            {
                Kind = kind,
                ProviderFileId = ReadAttachmentReference(item, providerMessageId, currentIndex),
                FileName = ReadNullableString(item, "filename") ?? $"attachment-{currentIndex + 1}",
                ContentType = ReadNullableString(item, "mime_type") ?? GetDefaultContentType(kind),
                SizeBytes = ReadNullableInt64(item, "size")
            });
        }

        return attachments;
    }

    private static string? ReadAttachmentReference(JsonElement attachment, string providerMessageId, int index)
    {
        if (providerMessageId.Length > 80
            || providerMessageId.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_')
            || !attachment.TryGetProperty("payload", out var payload)
            || payload.ValueKind != JsonValueKind.Object
            || !IsTrustedMaxCdnUrl(ReadNullableString(payload, "url")))
        {
            return null;
        }

        return $"{providerMessageId}:{index}";
    }

    private static string GetDefaultContentType(string type)
    {
        return type switch
        {
            "image" => "image/jpeg",
            "video" => "video/mp4",
            "audio" => "audio/mpeg",
            _ => "application/octet-stream"
        };
    }

    private static bool IsTrustedMaxCdnUrl(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && string.IsNullOrEmpty(uri.UserInfo)
            && uri.IsDefaultPort
            && (uri.Host.EndsWith(".okcdn.ru", StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith(".oneme.ru", StringComparison.OrdinalIgnoreCase));
    }

    private static string? ReadStringId(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    private static bool TryReadInt64(JsonElement element, string propertyName, out long value)
    {
        value = 0;
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt64(out value);
    }

    private static long? ReadNullableInt64(JsonElement element, string propertyName)
    {
        return TryReadInt64(element, propertyName, out var value) && value >= 0 ? value : null;
    }

    private static DateTime? ReadOccurredAt(JsonElement element)
    {
        if (!TryReadInt64(element, "timestamp", out var value)
            || value <= 0
            || value > DateTimeOffset.MaxValue.ToUnixTimeMilliseconds())
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeMilliseconds(value).UtcDateTime;
    }

    private static string? ReadNullableString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string? ReadSenderName(JsonElement sender)
    {
        var firstName = ReadNullableString(sender, "first_name");
        var lastName = ReadNullableString(sender, "last_name");
        var name = string.Join(' ', new[] { firstName, lastName }.Where(value => !string.IsNullOrWhiteSpace(value)));

        return name.Length == 0 ? null : name;
    }
}
