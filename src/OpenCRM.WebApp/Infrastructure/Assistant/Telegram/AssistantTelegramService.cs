using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Assistant;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.WebApp.Infrastructure.AiPilot;

namespace OpenCRM.WebApp.Infrastructure.Assistant.Telegram;

public sealed record AssistantTelegramLinkResult(string Url, DateTime ExpiresAt);

public sealed record AssistantTelegramBindingStatus(
    bool IsConfigured,
    bool IsConnected,
    Guid? OrganizationId,
    bool SpeechRepliesEnabled,
    bool IsBound,
    string? TelegramUserId,
    DateTime? ConnectedAt,
    string? BotUsername,
    bool CanViewOrganizationConnections);

public sealed record AssistantTelegramOrganizationConnection(
    string UserName,
    string TelegramUserId,
    DateTime ConnectedAt,
    bool IsCurrentUser);

public sealed record AssistantTelegramConnectionsPage(
    IReadOnlyList<AssistantTelegramOrganizationConnection> Items,
    int TotalCount,
    int Page,
    int PageSize);

public sealed class AssistantTelegramService(
    OpenCRMContext context,
    AssistantAccessService access,
    IOrganizationSubscriptionService subscriptions,
    AssistantService assistant,
    IAssistantTelegramApiClient telegram,
    IConfiguration configuration,
    AssistantOptions options,
    ILogger<AssistantTelegramService> logger)
{
    private const int TelegramStartParameterMaximumCharacters = 64;
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    private readonly AssistantTelegramConfiguration _configuration = new(configuration);

    public async Task<AssistantTelegramLinkResult> CreateLinkAsync(string userId, Guid organizationId, CancellationToken cancellationToken)
    {
        await this.EnsureOrganizationMemberAsync(userId, organizationId, cancellationToken);
        await access.EnsureAvailableAsync(new AssistantActor(userId, organizationId, "telegram"), cancellationToken);
        if (!this._configuration.IsConfigured)
        {
            throw new InvalidOperationException("Telegram-бот ещё не настроен сервером.");
        }

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var tokenHash = Hash(token);
        var expiresAt = DateTime.MinValue;
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            await this.LockUserRowAsync(userId, cancellationToken);
            var now = DateTime.UtcNow;
            expiresAt = now.AddMinutes(this._configuration.LinkTtlMinutes);
            await context.Set<AssistantTelegramLink>()
                .Where(item => item.UserId == userId && item.UsedAt == null && item.ExpiresAt > now)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ExpiresAt, now), cancellationToken);
            context.Add(new AssistantTelegramLink
            {
                TokenHash = tokenHash,
                UserId = userId,
                OrganizationId = organizationId,
                ExpiresAt = expiresAt
            });
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });

        var link = new Uri(this._configuration.BotLinkBaseUri, $"{Uri.EscapeDataString(this._configuration.BotUsername!)}?start={token}");
        return new AssistantTelegramLinkResult(link.ToString(), expiresAt);
    }

    public async Task<AssistantTelegramBindingStatus> GetStatusAsync(string userId, Guid organizationId, CancellationToken cancellationToken)
    {
        await this.EnsureOrganizationMemberAsync(userId, organizationId, cancellationToken, requireWorkspaceRead: false);
        var binding = await context.Set<AssistantTelegramBinding>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.UserId == userId && item.RevokedAt == null, cancellationToken);

        return new AssistantTelegramBindingStatus(
            this._configuration.IsConfigured,
            binding is not null && binding.SelectedOrganizationId == organizationId,
            binding?.SelectedOrganizationId,
            binding?.SpeechRepliesEnabled ?? false,
            binding is not null,
            binding?.TelegramUserId.ToString(CultureInfo.InvariantCulture),
            binding?.CreatedAt,
            this._configuration.IsConfigured ? this._configuration.BotUsername : null,
            await this.CanViewOrganizationConnectionsAsync(userId, organizationId, cancellationToken));
    }

    public async Task<AssistantTelegramConnectionsPage> GetOrganizationConnectionsAsync(
        string userId,
        Guid organizationId,
        int page,
        CancellationToken cancellationToken)
    {
        await this.EnsureOrganizationMemberAsync(userId, organizationId, cancellationToken);
        await access.EnsureAvailableAsync(new AssistantActor(userId, organizationId, "web"), cancellationToken);
        if (!await this.CanViewOrganizationConnectionsAsync(userId, organizationId, cancellationToken))
        {
            throw new UnauthorizedAccessException("Просматривать подключения Telegram могут только владелец и администратор организации.");
        }

        var pageSize = options.LookupPageSize;
        page = Math.Clamp(page, 1, int.MaxValue / pageSize);
        var connections = context.Set<AssistantTelegramBinding>().AsNoTracking()
            .Where(binding => binding.RevokedAt == null && binding.SelectedOrganizationId == organizationId
                && context.OrganizationMembers.Any(member => member.OrganizationId == organizationId && member.UserId == binding.UserId))
            .Join(context.ApplicationUsers.AsNoTracking(), binding => binding.UserId, user => user.Id,
                (binding, user) => new { Binding = binding, UserName = user.UserName });
        var totalCount = await connections.CountAsync(cancellationToken);
        var rows = await connections
            .OrderByDescending(item => item.Binding.CreatedAt)
            .ThenBy(item => item.Binding.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(item => new
            {
                item.UserName,
                item.Binding.TelegramUserId,
                item.Binding.CreatedAt,
                item.Binding.UserId
            })
            .ToListAsync(cancellationToken);
        var items = rows.Select(item => new AssistantTelegramOrganizationConnection(
            item.UserName ?? string.Empty,
            item.TelegramUserId.ToString(CultureInfo.InvariantCulture),
            item.CreatedAt,
            item.UserId == userId)).ToList();

        return new AssistantTelegramConnectionsPage(items, totalCount, page, pageSize);
    }

    public async Task DisconnectAsync(string userId, Guid organizationId, CancellationToken cancellationToken)
    {
        var strategy = context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            await this.LockUserRowAsync(userId, cancellationToken);
            var now = DateTime.UtcNow;
            await context.Set<AssistantTelegramLink>()
                .Where(item => item.UserId == userId && item.UsedAt == null && item.ExpiresAt > now)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ExpiresAt, now), cancellationToken);
            await context.Set<AssistantTelegramBinding>()
                .Where(item => item.UserId == userId && item.RevokedAt == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.RevokedAt, now)
                    .SetProperty(item => item.ConversationId, (Guid?)null), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }

    private async Task LockUserRowAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await context.ApplicationUsers
            .FromSqlInterpolated($"SELECT * FROM identity.\"AspNetUsers\" WHERE \"Id\" = {userId} FOR NO KEY UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (user is null)
        {
            throw new UnauthorizedAccessException("Пользователь не найден.");
        }
    }

    private async Task<bool> CanViewOrganizationConnectionsAsync(string userId, Guid organizationId, CancellationToken cancellationToken)
    {
        if (await context.Organizations.AsNoTracking()
            .AnyAsync(item => item.Id == organizationId && item.OwnerUserId == userId, cancellationToken))
        {
            return true;
        }

        return await context.OrganizationMembers.AsNoTracking().AnyAsync(item => item.OrganizationId == organizationId
            && item.UserId == userId && item.RoleCode == OrganizationRoleCodes.Admin, cancellationToken);
    }

    public async Task SetSpeechRepliesAsync(string userId, Guid organizationId, bool enabled, CancellationToken cancellationToken)
    {
        await this.EnsureOrganizationMemberAsync(userId, organizationId, cancellationToken, requireWorkspaceRead: false);
        var binding = await context.Set<AssistantTelegramBinding>()
            .SingleOrDefaultAsync(item => item.UserId == userId && item.SelectedOrganizationId == organizationId && item.RevokedAt == null, cancellationToken)
            ?? throw new InvalidOperationException("Telegram не подключён к этой организации.");
        binding.SpeechRepliesEnabled = enabled;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> EnqueueWebhookAsync(JsonElement update, string? suppliedSecret, CancellationToken cancellationToken)
    {
        if (!this._configuration.IsConfigured || !this._configuration.MatchesWebhookSecret(suppliedSecret))
        {
            return false;
        }

        if (JsonSerializer.SerializeToUtf8Bytes(update).Length > this._configuration.MaximumPayloadBytes)
        {
            return true;
        }

        var queueItem = ParseUpdate(update);
        if (queueItem is null)
        {
            return true;
        }

        var enabled = await context.Set<AssistantSettings>().AsNoTracking()
            .AnyAsync(item => item.Id == 1 && item.IsEnabled && item.TelegramEnabled, cancellationToken);
        if (!enabled)
        {
            return true;
        }

        return await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var settings = await context.Set<AssistantSettings>()
                .FromSqlRaw("SELECT * FROM \"AssistantSettings\" WHERE \"Id\" = 1 FOR UPDATE")
                .SingleAsync(cancellationToken);
            if (!settings.IsEnabled || !settings.TelegramEnabled ||
                await context.Set<AssistantTelegramUpdate>().AnyAsync(item => item.UpdateId == queueItem.UpdateId, cancellationToken))
            {
                return true;
            }

            var userFilter = JsonSerializer.Serialize(new { telegramUserId = queueItem.TelegramUserId }, s_json);
            var pending = await context.Set<AssistantTelegramUpdate>().CountAsync(item =>
                (item.Status == "pending" || item.Status == "processing") && EF.Functions.JsonContains(item.PayloadJson, userFilter), cancellationToken);
            if (pending >= this._configuration.MaximumPendingPerUser)
            {
                throw new InvalidOperationException("Очередь команд заполнена. Дождитесь выполнения предыдущих сообщений.");
            }

            context.Add(new AssistantTelegramUpdate
            {
                UpdateId = queueItem.UpdateId,
                PayloadJson = JsonSerializer.Serialize(queueItem, s_json)
            });
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return true;
        });
    }

    public async Task<bool> ProcessNextUpdateAsync(CancellationToken cancellationToken)
    {
        var updateId = await context.Set<AssistantTelegramUpdate>().AsNoTracking()
            .Where(item => item.Status == "pending")
            .OrderBy(item => item.CreatedAt)
            .Select(item => (long?)item.UpdateId)
            .FirstOrDefaultAsync(cancellationToken);
        if (!updateId.HasValue)
        {
            return false;
        }

        var claimed = await context.Set<AssistantTelegramUpdate>()
            .Where(item => item.UpdateId == updateId.Value && item.Status == "pending")
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Status, "processing")
                .SetProperty(item => item.StartedAt, DateTime.UtcNow), cancellationToken);
        if (claimed != 1)
        {
            return true;
        }

        var queued = await context.Set<AssistantTelegramUpdate>().AsNoTracking()
            .SingleAsync(item => item.UpdateId == updateId.Value, cancellationToken);
        var update = JsonSerializer.Deserialize<TelegramQueueItem>(queued.PayloadJson, s_json)
            ?? throw new InvalidOperationException("Telegram update queue payload is invalid.");
        try
        {
            await this.ProcessUpdateAsync(update, cancellationToken);
            await context.Set<AssistantTelegramUpdate>().Where(item => item.UpdateId == update.UpdateId && item.Status == "processing")
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Status, "completed")
                    .SetProperty(item => item.ResponseJson, "{\"processed\":true}")
                    .SetProperty(item => item.CompletedAt, DateTime.UtcNow), cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning("Не удалось обработать Telegram update {UpdateId}; ошибка {ErrorCode}, повторное выполнение отключено.",
                update.UpdateId,
                exception.GetType().Name);
            await context.Set<AssistantTelegramUpdate>().Where(item => item.UpdateId == update.UpdateId && item.Status == "processing")
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Status, "failed")
                    .SetProperty(item => item.ResponseJson, "{\"processed\":false}")
                    .SetProperty(item => item.ErrorCode, exception.GetType().Name)
                    .SetProperty(item => item.CompletedAt, DateTime.UtcNow), CancellationToken.None);
            try
            {
                await telegram.SendMessageAsync(update.ChatId,
                    "Не удалось подтвердить результат. Проверьте запись в CRM перед повтором; повторное выполнение этого обновления отключено.",
                    null,
                    CancellationToken.None);
            }
            catch (Exception sendException)
            {
                logger.LogWarning("Не удалось отправить сообщение об ошибке Telegram update {UpdateId}; ошибка {ErrorCode}.",
                    update.UpdateId,
                    sendException.GetType().Name);
            }
        }

        return true;
    }

    private async Task ProcessUpdateAsync(TelegramQueueItem update, CancellationToken cancellationToken)
    {
        if (update.StartTokenHash is not null)
        {
            await this.RedeemLinkAsync(update, cancellationToken);
            return;
        }

        if (update.CallbackQueryId is not null && update.CallbackData is not null)
        {
            await this.ProcessCallbackAsync(update, cancellationToken);
            return;
        }

        var binding = await this.FindBindingAsync(update, cancellationToken);
        if (binding is null)
        {
            await telegram.SendMessageAsync(update.ChatId, "Сначала подключите помощника в личном кабинете CRM.", null, cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(update.Text) && update.VoiceFileId is null)
        {
            await telegram.SendMessageAsync(update.ChatId, "Отправьте сообщение или голосовую команду. Команды: /new, /organizations, /voice.", null, cancellationToken);
            return;
        }

        await this.ProcessBoundMessageAsync(binding, update, cancellationToken);
    }

    private async Task RedeemLinkAsync(TelegramQueueItem update, CancellationToken cancellationToken)
    {
        var redeemed = await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var link = await context.Set<AssistantTelegramLink>()
                .FromSqlInterpolated($"SELECT * FROM \"AssistantTelegramLinks\" WHERE \"TokenHash\" = {update.StartTokenHash} AND \"UsedAt\" IS NULL AND \"ExpiresAt\" > {DateTime.UtcNow} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);
            if (link is null || update.TelegramUserId == 0 || update.ChatId != update.TelegramUserId)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            await this.EnsureOrganizationMemberAsync(link.UserId, link.OrganizationId, cancellationToken);
            await access.EnsureAvailableAsync(new AssistantActor(link.UserId, link.OrganizationId, "telegram"), cancellationToken);
            link.UsedAt = DateTime.UtcNow;
            var oldBindings = await context.Set<AssistantTelegramBinding>()
                .Where(item => item.RevokedAt == null && (item.UserId == link.UserId || item.TelegramUserId == update.TelegramUserId))
                .ToListAsync(cancellationToken);
            foreach (var binding in oldBindings)
            {
                binding.RevokedAt = DateTime.UtcNow;
                binding.ConversationId = null;
            }
            context.Add(new AssistantTelegramBinding
            {
                UserId = link.UserId,
                TelegramUserId = update.TelegramUserId,
                ChatId = update.ChatId,
                SelectedOrganizationId = link.OrganizationId
            });
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return true;
        });
        if (!redeemed)
        {
            await telegram.SendMessageAsync(update.ChatId, "Ссылка недействительна или истекла. Создайте новую ссылку в личном кабинете.", null, cancellationToken);

            return;
        }

        await telegram.SendMessageAsync(update.ChatId, "CRM подключён. Можно отправить голосовую или текстовую команду. /organizations — сменить организацию, /voice — включить или выключить голосовые ответы.", null, cancellationToken);
    }

    private async Task ProcessBoundMessageAsync(AssistantTelegramBinding binding, TelegramQueueItem update, CancellationToken cancellationToken)
    {
        if (update.MessageTooLong)
        {
            await telegram.SendMessageAsync(update.ChatId,
                $"Сообщение слишком длинное. Максимум — {this._configuration.MaximumTextCharacters} символов.",
                null,
                cancellationToken);
            return;
        }

        var command = update.Text?.Trim() ?? string.Empty;
        if (command.Equals("/organizations", StringComparison.OrdinalIgnoreCase))
        {
            await this.SendOrganizationsAsync(binding, cancellationToken);
            return;
        }
        if (command.Equals("/new", StringComparison.OrdinalIgnoreCase))
        {
            await this.CloseConversationAsync(binding, cancellationToken);
            await telegram.SendMessageAsync(update.ChatId, "Начал новый разговор. Отправьте задачу или запишите голосовое сообщение.", null, cancellationToken);
            return;
        }
        if (command.Equals("/voice", StringComparison.OrdinalIgnoreCase))
        {
            binding.SpeechRepliesEnabled = !binding.SpeechRepliesEnabled;
            await context.SaveChangesAsync(cancellationToken);
            await telegram.SendMessageAsync(update.ChatId, binding.SpeechRepliesEnabled ? "Голосовые ответы включены." : "Голосовые ответы выключены.", null, cancellationToken);
            return;
        }
        if (command.StartsWith("/org ", StringComparison.OrdinalIgnoreCase))
        {
            await this.SelectOrganizationAsync(binding, command[5..].Trim(), cancellationToken);
            return;
        }

        var actor = await this.GetActorAsync(binding, cancellationToken);
        if (update.VoiceFileId is not null)
        {
            if (update.VoiceDuration is int duration && duration > this._configuration.MaximumAudioDurationSeconds)
            {
                await telegram.SendMessageAsync(update.ChatId, $"Голосовое сообщение должно быть короче {this._configuration.MaximumAudioDurationSeconds} секунд.", null, cancellationToken);
                return;
            }
            var file = await telegram.DownloadVoiceAsync(update.VoiceFileId, this._configuration.MaximumAudioBytes, cancellationToken);
            await using var audio = new MemoryStream(file.Content, writable: false);
            command = (await assistant.TranscribeAsync(actor, audio, "telegram-voice.ogg", file.ContentType, cancellationToken)).Text;
        }

        var reply = await assistant.ChatAsync(actor, command, binding.ConversationId, cancellationToken);
        binding.ConversationId = reply.ConversationId;
        await context.SaveChangesAsync(cancellationToken);
        await this.SendAssistantReplyAsync(binding, actor, reply, cancellationToken);
    }

    private async Task ProcessCallbackAsync(TelegramQueueItem update, CancellationToken cancellationToken)
    {
        var binding = await this.FindBindingAsync(update, cancellationToken);
        var secret = this._configuration.WebhookSecret;
        if (binding is null || secret is null || !AssistantTelegramSecurity.TryValidateCallback(
            secret,
            update.TelegramUserId,
            update.CallbackData!,
            DateTime.UtcNow,
            this._configuration.CallbackTtlMinutes,
            out var command))
        {
            await telegram.AnswerCallbackAsync(update.CallbackQueryId!, "Действие устарело или недоступно.", cancellationToken);
            return;
        }

        var parts = command.Split(':');
        await telegram.AnswerCallbackAsync(update.CallbackQueryId!, "Обрабатываю…", cancellationToken);
        if (parts[0] == "o" && parts.Length == 2 && Guid.TryParseExact(parts[1], "N", out var organizationId))
        {
            await this.SetOrganizationAsync(binding, organizationId, cancellationToken);
            await telegram.SendMessageAsync(update.ChatId, "Организация выбрана.", null, cancellationToken);
            return;
        }

        if (parts.Length == 2 && (parts[0] is "c" or "x") && Guid.TryParseExact(parts[1], "N", out var draftId))
        {
            var currentDraft = await context.Set<AssistantDraft>().AsNoTracking().SingleOrDefaultAsync(item => item.Id == draftId &&
                item.UserId == binding.UserId && item.OrganizationId == binding.SelectedOrganizationId && item.Channel == "telegram" &&
                item.ConversationId == binding.ConversationId, cancellationToken);
            if (currentDraft is null)
            {
                await telegram.SendMessageAsync(update.ChatId, "Эта кнопка относится к прежнему разговору. Используйте последний черновик.", null, cancellationToken);

                return;
            }

            if (parts[0] == "x")
            {
                await this.CloseConversationAsync(binding, cancellationToken);
                await telegram.SendMessageAsync(update.ChatId, "Черновик отменён. Отправьте новую команду или используйте /new.", null, cancellationToken);
                return;
            }

            var actor = await this.GetActorAsync(binding, cancellationToken);
            var result = await assistant.ConfirmAsync(actor, draftId, true, cancellationToken);
            var url = await this.GetResultUrlAsync(actor, result, cancellationToken);
            const string completedMessage = "Действие выполнено. Подробности доступны в CRM.";
            await telegram.SendMessageAsync(update.ChatId, $"{completedMessage}\n{url}", null, cancellationToken);
            await this.TrySendSpeechAsync(binding, actor, completedMessage, cancellationToken);
            return;
        }

        if (parts.Length == 4 && parts[0] == "a" && Guid.TryParseExact(parts[1], "N", out var conversationId)
            && int.TryParse(parts[2], out var choiceIndex))
        {
            var actor = await this.GetActorAsync(binding, cancellationToken);
            var choice = await this.GetPendingChoiceAsync(binding, conversationId, choiceIndex, parts[3], cancellationToken);
            var reply = await assistant.ChooseAsync(actor, conversationId, choice.Field, choice.Value, cancellationToken);
            binding.ConversationId = reply.ConversationId;
            await context.SaveChangesAsync(cancellationToken);
            await this.SendAssistantReplyAsync(binding, actor, reply, cancellationToken);
            return;
        }

        if (parts.Length == 4 && parts[0] == "p" && Guid.TryParseExact(parts[1], "N", out conversationId)
            && int.TryParse(parts[2], out var offset))
        {
            await this.GetActorAsync(binding, cancellationToken);
            await this.SendChoicePageAsync(binding, conversationId, offset, parts[3], cancellationToken);
            return;
        }

        await telegram.SendMessageAsync(update.ChatId, "Кнопка устарела. Продолжите разговор текстом.", null, cancellationToken);
    }

    private async Task SendAssistantReplyAsync(AssistantTelegramBinding binding, AssistantActor actor, AssistantReply reply, CancellationToken cancellationToken)
    {
        var text = reply.Message;
        IReadOnlyList<IReadOnlyList<AssistantTelegramButton>>? buttons = null;
        if (reply.Draft is { } draft)
        {
            text = $"{reply.Message}\n\n{draft.Target}\n{string.Join("\n", draft.Changes.Concat(draft.Warnings).Select(change => $"• {change}"))}";
            if (text.Length > this._configuration.MaximumTextCharacters)
            {
                await telegram.SendMessageAsync(binding.ChatId,
                    "Черновик слишком длинный для полного показа в Telegram. Подтверждение здесь отключено. Откройте CRM и подготовьте действие на сайте.",
                    null, cancellationToken);

                return;
            }

            buttons = [[
                this.Button(draft.ReadOnly ? "Показать" : "Выполнить", $"c:{draft.Id:N}", binding.TelegramUserId),
                this.Button("Отменить", $"x:{draft.Id:N}", binding.TelegramUserId)
            ]];
        }
        else if (reply.Clarification is { Choices.Count: > 0 } clarification)
        {
            var choiceHash = AssistantTelegramSecurity.HashChoice(clarification);
            var pageSize = this._configuration.MaximumChoicesPerPage;
            var visible = clarification.Choices.Take(pageSize)
                .Select((choice, index) => (IReadOnlyList<AssistantTelegramButton>)[this.Button(choice.Label, $"a:{reply.ConversationId:N}:{index}:{choiceHash}", binding.TelegramUserId)])
                .ToList();
            if (clarification.Choices.Count > pageSize)
            {
                visible.Add([
                    this.Button("Дальше →", $"p:{reply.ConversationId:N}:{pageSize}:{choiceHash}", binding.TelegramUserId)
                ]);
            }
            buttons = visible;
        }

        await telegram.SendMessageAsync(binding.ChatId, text, buttons, cancellationToken);
        await this.TrySendSpeechAsync(binding, actor, text, cancellationToken);
    }

    private async Task<string> GetResultUrlAsync(AssistantActor actor, AssistantActionResult result, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(configuration["AppSettings:BaseUrl"], UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(baseUri.UserInfo))
        {
            throw new InvalidOperationException("Не настроен HTTPS-адрес приложения.");
        }

        var scenario = await context.Organizations.AsNoTracking().Where(item => item.Id == actor.OrganizationId)
            .Select(item => item.PrimaryScenarioCode).SingleAsync(cancellationToken);
        var section = scenario == "detailing" ? "detailing" : "autoservice";
        var fragment = result.MaintenanceId.HasValue ? $"#maintenance/{result.MaintenanceId:D}"
            : result.VehicleId.HasValue ? $"#vehicles/{result.VehicleId:D}"
            : result.ClientId.HasValue ? $"#clients/{result.ClientId:D}" : string.Empty;

        return new Uri(baseUri, $"{section}/{actor.OrganizationId:D}{fragment}").AbsoluteUri;
    }

    private async Task SendChoicePageAsync(AssistantTelegramBinding binding, Guid conversationId, int offset, string expectedChoiceHash, CancellationToken cancellationToken)
    {
        var conversation = await context.Set<AssistantConversation>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == conversationId && item.UserId == binding.UserId
                && item.OrganizationId == binding.SelectedOrganizationId && item.Channel == "telegram" && !item.IsClosed,
                cancellationToken);
        var selection = conversation is null ? null : JsonSerializer.Deserialize<AssistantConversationSelection>(conversation.SelectionJson, s_json);
        var choice = selection?.PendingChoice;
        if (choice is null || binding.ConversationId != conversationId || conversation is null || conversation.ExpiresAt <= DateTime.UtcNow
            || AssistantTelegramSecurity.HashChoice(choice) != expectedChoiceHash || offset < 0 || offset >= choice.Choices.Count)
        {
            await telegram.SendMessageAsync(binding.ChatId, "Список выбора устарел. Отправьте команду заново.", null, cancellationToken);
            return;
        }

        var pageSize = this._configuration.MaximumChoicesPerPage;
        var end = Math.Min(offset + pageSize, choice.Choices.Count);
        var rows = choice.Choices.Skip(offset).Take(pageSize)
            .Select((option, index) => (IReadOnlyList<AssistantTelegramButton>)[this.Button(option.Label, $"a:{conversationId:N}:{offset + index}:{expectedChoiceHash}", binding.TelegramUserId)])
            .ToList();
        var navigation = new List<AssistantTelegramButton>();
        if (offset > 0)
        {
            navigation.Add(this.Button("← Назад", $"p:{conversationId:N}:{Math.Max(0, offset - pageSize)}:{expectedChoiceHash}", binding.TelegramUserId));
        }
        if (end < choice.Choices.Count)
        {
            navigation.Add(this.Button("Дальше →", $"p:{conversationId:N}:{end}:{expectedChoiceHash}", binding.TelegramUserId));
        }
        if (navigation.Count > 0)
        {
            rows.Add(navigation);
        }

        await telegram.SendMessageAsync(binding.ChatId, choice.Message, rows, cancellationToken);
    }

    private async Task<(string Field, string Value)> GetPendingChoiceAsync(AssistantTelegramBinding binding, Guid conversationId, int choiceIndex, string expectedChoiceHash, CancellationToken cancellationToken)
    {
        var conversation = await context.Set<AssistantConversation>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == conversationId && item.UserId == binding.UserId
                && item.OrganizationId == binding.SelectedOrganizationId && item.Channel == "telegram" && !item.IsClosed,
                cancellationToken);
        var selection = conversation is null ? null : JsonSerializer.Deserialize<AssistantConversationSelection>(conversation.SelectionJson, s_json);
        var choice = selection?.PendingChoice;
        if (choice is null || binding.ConversationId != conversationId || conversation is null || conversation.ExpiresAt <= DateTime.UtcNow
            || AssistantTelegramSecurity.HashChoice(choice) != expectedChoiceHash || choiceIndex < 0 || choiceIndex >= choice.Choices.Count)
        {
            throw new InvalidOperationException("Кнопка устарела. Отправьте команду заново.");
        }

        return (choice.Field, choice.Choices[choiceIndex].Value);
    }

    private async Task SendOrganizationsAsync(AssistantTelegramBinding binding, CancellationToken cancellationToken)
    {
        var organizations = await this.GetAvailableOrganizationsAsync(binding.UserId, cancellationToken);
        if (organizations.Count == 0)
        {
            await telegram.SendMessageAsync(binding.ChatId, "Нет доступных организаций с включённым Telegram-помощником.", null, cancellationToken);
            return;
        }

        var rows = organizations.Select(organization => (IReadOnlyList<AssistantTelegramButton>)[this.Button(
            organization.Name,
            $"o:{organization.Id:N}",
            binding.TelegramUserId)]).ToArray();
        await telegram.SendMessageAsync(binding.ChatId, "Выберите организацию для разговора:", rows, cancellationToken);
    }

    private async Task<IReadOnlyList<(Guid Id, string Name)>> GetAvailableOrganizationsAsync(string userId, CancellationToken cancellationToken)
    {
        var candidates = await context.OrganizationMembers.AsNoTracking()
            .Where(item => item.UserId == userId)
            .Select(item => new { item.OrganizationId, item.Organization.Name })
            .Distinct()
            .ToListAsync(cancellationToken);
        var organizations = new List<(Guid Id, string Name)>();
        foreach (var candidate in candidates)
        {
            try
            {
                await access.EnsureAvailableAsync(new AssistantActor(userId, candidate.OrganizationId, "telegram"), cancellationToken);
                organizations.Add((candidate.OrganizationId, candidate.Name));
            }
            catch (InvalidOperationException)
            {
                continue;
            }
        }

        return organizations;
    }

    private async Task SelectOrganizationAsync(AssistantTelegramBinding binding, string value, CancellationToken cancellationToken)
    {
        var organizations = await this.GetAvailableOrganizationsAsync(binding.UserId, cancellationToken);
        Guid organizationId;
        if (Guid.TryParse(value, out var selectedId))
        {
            organizationId = selectedId;
        }
        else if (int.TryParse(value, out var number) && number > 0 && number <= organizations.Count)
        {
            organizationId = organizations[number - 1].Id;
        }
        else
        {
            await this.SendOrganizationsAsync(binding, cancellationToken);
            return;
        }

        if (organizations.All(item => item.Id != organizationId))
        {
            throw new UnauthorizedAccessException("Организация недоступна.");
        }

        await this.SetOrganizationAsync(binding, organizationId, cancellationToken);
        await telegram.SendMessageAsync(binding.ChatId, "Организация выбрана.", null, cancellationToken);
    }

    private async Task SetOrganizationAsync(AssistantTelegramBinding binding, Guid organizationId, CancellationToken cancellationToken)
    {
        var actor = new AssistantActor(binding.UserId, organizationId, "telegram");
        await this.EnsureOrganizationMemberAsync(binding.UserId, organizationId, cancellationToken);
        await access.EnsureAvailableAsync(actor, cancellationToken);
        await this.CloseConversationAsync(binding, cancellationToken);
        binding.SelectedOrganizationId = organizationId;
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task CloseConversationAsync(AssistantTelegramBinding binding, CancellationToken cancellationToken)
    {
        if (binding.ConversationId.HasValue && binding.SelectedOrganizationId.HasValue)
        {
            try
            {
                await assistant.CloseAsync(new AssistantActor(binding.UserId, binding.SelectedOrganizationId.Value, "telegram"), binding.ConversationId.Value, cancellationToken);
            }
            catch (InvalidOperationException)
            {
                await this.CloseConversationWithoutAssistantAccessAsync(binding, cancellationToken);
            }
        }

        binding.ConversationId = null;
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task CloseConversationWithoutAssistantAccessAsync(AssistantTelegramBinding binding, CancellationToken cancellationToken)
    {
        if (!binding.ConversationId.HasValue || !binding.SelectedOrganizationId.HasValue)
        {
            return;
        }

        var conversationId = binding.ConversationId.Value;
        var now = DateTime.UtcNow;
        var conversation = await context.Set<AssistantConversation>().SingleOrDefaultAsync(item => item.Id == conversationId
            && item.UserId == binding.UserId && item.OrganizationId == binding.SelectedOrganizationId && item.Channel == "telegram", cancellationToken);
        if (conversation is null || conversation.IsClosed)
        {
            return;
        }
        if (conversation.ProcessingUntil.HasValue && conversation.ProcessingUntil > now)
        {
            throw new InvalidOperationException("Помощник ещё обрабатывает сообщение. Дождитесь ответа и повторите команду.");
        }

        await context.Set<AssistantDraft>().Where(item => item.ConversationId == conversationId && item.UserId == binding.UserId
            && item.OrganizationId == binding.SelectedOrganizationId && item.Channel == "telegram" && item.Status == "pending")
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Status, "superseded"), cancellationToken);
        conversation.IsClosed = true;
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<AssistantActor> GetActorAsync(AssistantTelegramBinding binding, CancellationToken cancellationToken)
    {
        if (!binding.SelectedOrganizationId.HasValue)
        {
            throw new InvalidOperationException("Выберите организацию командой /organizations.");
        }
        var actor = new AssistantActor(binding.UserId, binding.SelectedOrganizationId.Value, "telegram");
        await access.EnsureAvailableAsync(actor, cancellationToken);
        return actor;
    }

    private async Task<AssistantTelegramBinding?> FindBindingAsync(TelegramQueueItem update, CancellationToken cancellationToken)
    {
        if (update.TelegramUserId == 0 || update.ChatId != update.TelegramUserId)
        {
            return null;
        }

        return await context.Set<AssistantTelegramBinding>().SingleOrDefaultAsync(item => item.TelegramUserId == update.TelegramUserId
            && item.ChatId == update.ChatId && item.RevokedAt == null, cancellationToken);
    }

    private async Task EnsureOrganizationMemberAsync(string userId, Guid organizationId, CancellationToken cancellationToken, bool requireWorkspaceRead = true)
    {
        var isMember = await context.OrganizationMembers.AsNoTracking()
            .AnyAsync(item => item.UserId == userId && item.OrganizationId == organizationId, cancellationToken);
        if (!isMember)
        {
            throw new UnauthorizedAccessException("Пользователь не состоит в этой организации.");
        }
        if (requireWorkspaceRead)
        {
            await subscriptions.EnsureWorkspaceCanReadAsync(userId, organizationId, OrganizationPermissionCodes.WorkspaceView);
        }
    }

    private async Task TrySendSpeechAsync(AssistantTelegramBinding binding, AssistantActor actor, string text, CancellationToken cancellationToken)
    {
        if (!binding.SpeechRepliesEnabled)
        {
            return;
        }

        var enabled = await context.Set<AssistantSettings>().AsNoTracking()
            .AnyAsync(item => item.Id == 1 && item.IsEnabled && item.TelegramEnabled && item.SpeechRepliesEnabled, cancellationToken);
        if (!enabled)
        {
            return;
        }

        try
        {
            var speech = await assistant.SpeakAsync(actor, text, cancellationToken);
            await telegram.SendVoiceAsync(binding.ChatId, speech.Content, speech.ContentType, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning("Не удалось отправить голосовой ответ Telegram; ошибка {ErrorCode}, текстовый ответ сохранён.", exception.GetType().Name);
        }
    }

    private AssistantTelegramButton Button(string label, string command, long telegramUserId)
    {
        var secret = this._configuration.WebhookSecret ?? throw new InvalidOperationException("Telegram webhook secret is not configured.");
        return new AssistantTelegramButton(
            label,
            AssistantTelegramSecurity.CreateCallback(secret, telegramUserId, command, DateTime.UtcNow, this._configuration.CallbackTtlMinutes));
    }

    private TelegramQueueItem? ParseUpdate(JsonElement update)
    {
        if (update.ValueKind != JsonValueKind.Object || !update.TryGetProperty("update_id", out var idElement) || !idElement.TryGetInt64(out var updateId))
        {
            return null;
        }

        if (update.TryGetProperty("callback_query", out var callback) && callback.ValueKind == JsonValueKind.Object)
        {
            var from = callback.TryGetProperty("from", out var callbackFrom) ? ReadId(callbackFrom, "id") : 0;
            var message = callback.TryGetProperty("message", out var callbackMessage) ? callbackMessage : default;
            if (from == 0 || message.ValueKind != JsonValueKind.Object || !IsPrivateChat(message, out var chatId) || chatId != from)
            {
                return null;
            }
            return new TelegramQueueItem
            {
                UpdateId = updateId,
                TelegramUserId = from,
                ChatId = chatId,
                CallbackQueryId = ReadString(callback, "id"),
                CallbackData = ReadString(callback, "data")
            };
        }

        if (!update.TryGetProperty("message", out var telegramMessage) || telegramMessage.ValueKind != JsonValueKind.Object
            || !IsPrivateChat(telegramMessage, out var privateChatId))
        {
            return null;
        }
        var senderId = telegramMessage.TryGetProperty("from", out var fromElement) ? ReadId(fromElement, "id") : 0;
        if (senderId == 0 || senderId != privateChatId)
        {
            return null;
        }

        var text = ReadString(telegramMessage, "text");
        var messageTooLong = text?.Length > this._configuration.MaximumTextCharacters;
        string? startTokenHash = null;
        if (text?.StartsWith("/start ", StringComparison.Ordinal) == true)
        {
            var token = text[7..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(token) && token.Length <= TelegramStartParameterMaximumCharacters)
            {
                startTokenHash = Hash(token);
            }
            text = "/start";
        }

        var voice = telegramMessage.TryGetProperty("voice", out var voiceElement) && voiceElement.ValueKind == JsonValueKind.Object
            ? voiceElement
            : default;
        return new TelegramQueueItem
        {
            UpdateId = updateId,
            TelegramUserId = senderId,
            ChatId = privateChatId,
            Text = messageTooLong ? null : text,
            MessageTooLong = messageTooLong,
            StartTokenHash = startTokenHash,
            VoiceFileId = voice.ValueKind == JsonValueKind.Object ? ReadString(voice, "file_id") : null,
            VoiceDuration = voice.ValueKind == JsonValueKind.Object ? ReadInt(voice, "duration") : null
        };
    }

    private static bool IsPrivateChat(JsonElement message, out long chatId)
    {
        chatId = 0;
        if (!message.TryGetProperty("chat", out var chat) || chat.ValueKind != JsonValueKind.Object
            || ReadString(chat, "type") != "private")
        {
            return false;
        }
        chatId = ReadId(chat, "id");
        return chatId != 0;
    }

    private static long ReadId(JsonElement element, string property) => element.TryGetProperty(property, out var value) && value.TryGetInt64(out var id) ? id : 0;
    private static int? ReadInt(JsonElement element, string property) => element.TryGetProperty(property, out var value) && value.TryGetInt32(out var result) ? result : null;
    private static string? ReadString(JsonElement element, string property) => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class TelegramQueueItem
    {
        public long UpdateId { get; init; }
        public long TelegramUserId { get; init; }
        public long ChatId { get; init; }
        public string? Text { get; init; }
        public bool MessageTooLong { get; init; }
        public string? StartTokenHash { get; init; }
        public string? VoiceFileId { get; init; }
        public int? VoiceDuration { get; init; }
        public string? CallbackQueryId { get; init; }
        public string? CallbackData { get; init; }
    }
}
