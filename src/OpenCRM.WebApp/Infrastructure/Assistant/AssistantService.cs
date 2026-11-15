using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain.Assistant;
using OpenCRM.Infrastructure.Data;
using OpenCRM.WebApp.Infrastructure.AiPilot;
using OpenCRM.Modules.Vehicles.Contracts;

namespace OpenCRM.WebApp.Infrastructure.Assistant;

public sealed class AssistantService(
    OpenCRMContext context,
    AssistantAccessService access,
    AssistantProviderContext providerContext,
    AiPilotConversationService engine,
    RouterAiPilotClient router,
    IAssistantActionExecutor executor,
    ILogger<AssistantService> logger,
    AssistantOptions options,
    IVehicleRegistrationPlatePolicy? registrationPlates = null)
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    public async Task<AssistantReply> ChatAsync(
        AssistantActor actor,
        string text,
        Guid? conversationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > options.MaximumMessageCharacters)
        {
            throw new InvalidOperationException($"Сообщение должно содержать от 1 до {options.MaximumMessageCharacters} символов.");
        }

        await this.ConfigureProviderAsync(actor, cancellationToken);
        var conversation = await this.GetConversationAsync(actor, conversationId, cancellationToken);
        var lease = await this.ClaimConversationAsync(conversation, cancellationToken);
        try
        {
            await this.InvalidateDraftsAsync(conversation.Id, cancellationToken);
            await this.RestoreAsync(actor, conversation, cancellationToken);
            var previousSelection = JsonSerializer.Deserialize<AssistantConversationSelection>(conversation.SelectionJson, s_json) ?? new();
            providerContext.ConversationId = conversation.Id;
            var zone = await this.GetTimeZoneAsync(actor.OrganizationId, cancellationToken);
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));
            var parsed = await engine.ChatAsync(actor.UserId, actor.OrganizationId, conversation.Id, text, today, cancellationToken);
            var snapshot = engine.Export(actor.UserId, actor.OrganizationId, conversation.Id);
            var selection = parsed.ContinuesPendingCommand ? previousSelection : new AssistantConversationSelection();
            selection.PendingChoice = null;
            AssistantReply reply;
            if (parsed.Draft is not null)
            {
                reply = await this.PrepareReplyAsync(actor, conversation, snapshot, parsed.Draft.Command, selection, parsed.Message, parsed.Cost, cancellationToken);
            }
            else
            {
                conversation.SelectionJson = JsonSerializer.Serialize(selection, s_json);
                reply = new AssistantReply(conversation.Id, parsed.Message, parsed.AwaitingAnswer, Cost: parsed.Cost);
                await this.PersistAsync(conversation, snapshot, cancellationToken);
            }

            return reply with { MessageId = await this.LastAssistantMessageIdAsync(conversation.Id, cancellationToken) };
        }
        finally
        {
            await this.ReleaseConversationAsync(conversation.Id, lease);
        }
    }

    public async Task<AssistantReply> ChooseAsync(
        AssistantActor actor,
        Guid conversationId,
        string field,
        string value,
        CancellationToken cancellationToken)
    {
        await this.ConfigureProviderAsync(actor, cancellationToken);
        var conversation = await this.GetConversationAsync(actor, conversationId, cancellationToken);
        var lease = await this.ClaimConversationAsync(conversation, cancellationToken);
        try
        {
            var selection = JsonSerializer.Deserialize<AssistantConversationSelection>(conversation.SelectionJson, s_json) ?? new();
            var pending = selection.PendingChoice;
            if (pending is null || pending.Field != field || !pending.Choices.Any(choice => choice.Value == value))
            {
                throw new InvalidOperationException("Выбор устарел или не относится к этому разговору. Отправьте команду заново.");
            }

            var snapshot = await this.RestoreAsync(actor, conversation, cancellationToken);
            var command = snapshot.PendingCommand ?? snapshot.PendingDraft?.Command
                ?? throw new InvalidOperationException("Уточняемая команда больше недоступна.");
            if (field == "maintenanceId")
            {
                selection.MaintenanceId = Guid.Parse(value);
            }
            else
            {
                selection.Choices[field] = value;
            }

            selection.PendingChoice = null;
            var reply = await this.PrepareReplyAsync(actor, conversation, snapshot, command, selection,
                "Выбор учтён. Проверьте подготовленное действие.", null, cancellationToken);

            return reply with { MessageId = await this.LastAssistantMessageIdAsync(conversation.Id, cancellationToken) };
        }
        finally
        {
            await this.ReleaseConversationAsync(conversation.Id, lease);
        }
    }

    public async Task<AssistantActionResult> ConfirmAsync(
        AssistantActor actor,
        Guid draftId,
        bool confirmed,
        CancellationToken cancellationToken)
    {
        await access.EnsureAvailableAsync(actor, cancellationToken);
        var draft = await context.Set<AssistantDraft>().AsNoTracking().SingleOrDefaultAsync(item => item.Id == draftId
            && item.UserId == actor.UserId && item.OrganizationId == actor.OrganizationId && item.Channel == actor.Channel, cancellationToken)
            ?? throw new InvalidOperationException("Черновик не найден.");
        if (draft.Status == "completed")
        {
            return JsonSerializer.Deserialize<AssistantActionResult>(draft.ResultJson!, s_json)!;
        }

        if (draft.Status != "pending" || draft.ExpiresAt <= DateTime.UtcNow)
        {
            throw new InvalidOperationException("Черновик истёк, уже выполняется или требует проверки результата. Повторное выполнение заблокировано.");
        }

        if (draft.Destructive && !confirmed)
        {
            throw new InvalidOperationException("Подтвердите перечисленные удаления или отмену.");
        }

        this.EnsureRegistrationPlate(JsonSerializer.Deserialize<AiPilotCommand>(draft.CommandJson, s_json)!);

        await access.EnsureAllowedAsync(actor, draft.Action, cancellationToken);
        var conversation = await this.GetConversationAsync(actor, draft.ConversationId, cancellationToken);
        var lease = await this.ClaimConversationAsync(conversation, cancellationToken);
        try
        {
            return await this.ExecuteDraftAsync(actor, draft, cancellationToken);
        }
        finally
        {
            await this.ReleaseConversationAsync(conversation.Id, lease);
        }
    }

    private async Task<AssistantActionResult> ExecuteDraftAsync(AssistantActor actor, AssistantDraft draft, CancellationToken cancellationToken)
    {
        var claimed = await context.Set<AssistantDraft>().Where(item => item.Id == draft.Id && item.Status == "pending" && item.ExpiresAt > DateTime.UtcNow)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Status, "executing"), cancellationToken);
        if (claimed != 1)
        {
            throw new InvalidOperationException("Команда уже выполняется. Дождитесь результата.");
        }

        AssistantActionResult result;
        try
        {
            var command = JsonSerializer.Deserialize<AiPilotCommand>(draft.CommandJson, s_json)!;
            var selection = JsonSerializer.Deserialize<AssistantConversationSelection>(draft.SelectionJson, s_json) ?? new();
            var plan = await executor.PrepareAsync(command, actor, selection.ToActionSelection(), cancellationToken);
            if (Hash(plan.Snapshot) != draft.Snapshot)
            {
                throw new InvalidOperationException("Данные изменились после подготовки. Отправьте команду ещё раз и проверьте новый черновик.");
            }

            await access.EnsureAllowedAsync(actor, draft.Action, cancellationToken);
            result = await plan.ExecuteAsync(cancellationToken);
            await context.Set<AssistantDraft>().Where(item => item.Id == draft.Id && item.Status == "executing")
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Status, "completed")
                    .SetProperty(item => item.ResultJson, JsonSerializer.Serialize(result, s_json))
                    .SetProperty(item => item.CompletedAt, DateTime.UtcNow), CancellationToken.None);
        }
        catch (Exception exception)
        {
            var message = exception is InvalidOperationException ? exception.Message : "Не удалось подтвердить результат действия. Проверьте записи в CRM перед повтором.";
            message = message[..Math.Min(message.Length, options.MaximumMessageCharacters)];
            await context.Set<AssistantDraft>().Where(item => item.Id == draft.Id && item.Status == "executing")
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Status, "failed")
                    .SetProperty(item => item.FailureMessage, message)
                    .SetProperty(item => item.CompletedAt, DateTime.UtcNow), CancellationToken.None);
            await this.TryAppendExecutionFeedbackAsync(actor, draft.ConversationId, message, false);
            throw new InvalidOperationException(message + " Повторное выполнение этого черновика заблокировано; проверьте результат в CRM.");
        }

        await this.TryAppendExecutionFeedbackAsync(actor, draft.ConversationId, result.Message, true);

        return result;
    }

    public async Task<AiPilotTranscription> TranscribeAsync(
        AssistantActor actor,
        Stream audio,
        string fileName,
        string contentType,
        CancellationToken cancellationToken)
    {
        await this.ConfigureProviderAsync(actor, cancellationToken);

        return await router.TranscribeAsync(audio, fileName, contentType, cancellationToken);
    }

    public async Task CloseAsync(AssistantActor actor, Guid conversationId, CancellationToken cancellationToken)
    {
        await access.EnsureAvailableAsync(actor, cancellationToken);
        var conversation = await this.GetConversationAsync(actor, conversationId, cancellationToken);
        var lease = await this.ClaimConversationAsync(conversation, cancellationToken);
        try
        {
            await this.InvalidateDraftsAsync(conversation.Id, cancellationToken);
            conversation.IsClosed = true;
            await context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            await this.ReleaseConversationAsync(conversation.Id, lease);
        }
    }

    public async Task<AssistantSpeechOutput> SpeakAsync(AssistantActor actor, string text, CancellationToken cancellationToken, Guid? conversationId = null)
    {
        await this.ConfigureProviderAsync(actor, cancellationToken);
        providerContext.ConversationId = conversationId;

        return await router.SynthesizeAsync(text, cancellationToken);
    }

    private Task<Guid?> LastAssistantMessageIdAsync(Guid conversationId, CancellationToken cancellationToken) =>
        context.Set<AssistantMessage>().AsNoTracking().Where(item => item.ConversationId == conversationId && item.Role == "assistant")
            .OrderByDescending(item => item.Sequence).Select(item => (Guid?)item.Id).FirstOrDefaultAsync(cancellationToken);

    private async Task ConfigureProviderAsync(AssistantActor actor, CancellationToken cancellationToken)
    {
        providerContext.Settings = await access.EnsureAvailableAsync(actor, cancellationToken);
        providerContext.Actor = actor;
        providerContext.Actions = await access.GetActionsAsync(actor, cancellationToken);
    }

    private async Task<AssistantReply> PrepareReplyAsync(
        AssistantActor actor,
        AssistantConversation conversation,
        AiPilotConversationSnapshot snapshot,
        AiPilotCommand command,
        AssistantConversationSelection selection,
        string message,
        decimal? cost,
        CancellationToken cancellationToken)
    {
        try
        {
            this.EnsureRegistrationPlate(command);
            var plan = await executor.PrepareAsync(command, actor, selection.ToActionSelection(), cancellationToken);
            var draft = new AssistantDraft
            {
                ConversationId = conversation.Id,
                OrganizationId = actor.OrganizationId,
                UserId = actor.UserId,
                Channel = actor.Channel,
                Action = command.Action,
                CommandJson = JsonSerializer.Serialize(command, s_json),
                SelectionJson = JsonSerializer.Serialize(selection, s_json),
                Snapshot = Hash(plan.Snapshot),
                Destructive = plan.Destructive,
                ExpiresAt = DateTime.UtcNow.AddMinutes(options.DraftTtlMinutes)
            };
            var preview = new AssistantDraftPreview(draft.Id, command, plan.Target, plan.Target, plan.Changes,
                plan.Destructive, plan.ReadOnly, plan.Data, AiPilotConversationService.GetWarnings(command), cost);
            draft.PreviewJson = JsonSerializer.Serialize(preview, s_json);
            context.Add(draft);
            selection.PendingChoice = null;
            conversation.SelectionJson = JsonSerializer.Serialize(selection, s_json);
            snapshot = snapshot with
            {
                PendingDraft = new AiPilotChatDraft(draft.Id.ToString("N"), command, plan.Target, preview.Warnings, cost),
                PendingCommand = null,
                RequestedSlot = null
            };
            await this.PersistAsync(conversation, snapshot, cancellationToken);

            return new AssistantReply(conversation.Id, message, false, preview, Cost: cost);
        }
        catch (AssistantClarificationException exception)
        {
            var text = exception.Message[..Math.Min(exception.Message.Length, options.MaximumMessageCharacters)];
            var choices = exception.Choices.Take(options.MaximumClarificationChoices).Select(choice => new AssistantChoice(choice.Value,
                choice.Label[..Math.Min(choice.Label.Length, 300)])).ToArray();
            var clarification = new AssistantClarification(exception.Field, text, choices);
            selection.PendingChoice = clarification;
            conversation.SelectionJson = JsonSerializer.Serialize(selection, s_json);
            var messages = snapshot.Messages.Append(new AiPilotConversationMessage("assistant", text)).ToArray();
            snapshot = snapshot with
            {
                Messages = messages,
                PendingCommand = command,
                PendingDraft = null,
                RequestedSlot = command.Action == "book" && exception.Field.StartsWith("book.price:", StringComparison.Ordinal) ||
                    exception.Field is "registrationPlate.confirm" or "parameter:newRegistrationPlate" or "parameter:vehicleKind" ? exception.Field : null
            };
            await this.PersistAsync(conversation, snapshot, cancellationToken);

            return new AssistantReply(conversation.Id, text, true, Clarification: clarification, Cost: cost);
        }
        catch (InvalidOperationException exception)
        {
            var text = exception.Message[..Math.Min(exception.Message.Length, options.MaximumMessageCharacters)];
            var messages = snapshot.Messages.Append(new AiPilotConversationMessage("assistant", text)).ToArray();
            snapshot = snapshot with { Messages = messages, PendingCommand = command, PendingDraft = null, RequestedSlot = null };
            await this.PersistAsync(conversation, snapshot, cancellationToken);

            return new AssistantReply(conversation.Id, text, true, Cost: cost);
        }
    }

    private void EnsureRegistrationPlate(AiPilotCommand command)
    {
        var issue = registrationPlates is null ? null : AiPilotRegistrationPlateGuard.GetClarification(command, registrationPlates);
        if (issue is not null)
        {
            throw new AssistantClarificationException(issue.Message, issue.Field, issue.Choices);
        }
    }

    private async Task<AssistantConversation> GetConversationAsync(AssistantActor actor, Guid? id, CancellationToken cancellationToken)
    {
        if (id.HasValue)
        {
            return await context.Set<AssistantConversation>().SingleOrDefaultAsync(item => item.Id == id && item.UserId == actor.UserId
                && item.OrganizationId == actor.OrganizationId && item.Channel == actor.Channel && !item.IsClosed
                && item.ExpiresAt > DateTime.UtcNow, cancellationToken)
                ?? throw new InvalidOperationException("Разговор истёк или недоступен. Начните новый разговор.");
        }

        var conversation = new AssistantConversation
        {
            OrganizationId = actor.OrganizationId,
            UserId = actor.UserId,
            Channel = actor.Channel,
            ExpiresAt = DateTime.UtcNow.AddMinutes(options.ConversationTtlMinutes)
        };
        context.Add(conversation);
        await context.SaveChangesAsync(cancellationToken);

        return conversation;
    }

    private async Task<Guid> ClaimConversationAsync(AssistantConversation conversation, CancellationToken cancellationToken)
    {
        var token = Guid.NewGuid();
        var claimed = await context.Set<AssistantConversation>().Where(item => item.Id == conversation.Id
            && (!item.ProcessingUntil.HasValue || item.ProcessingUntil < DateTime.UtcNow))
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ProcessingToken, token)
                .SetProperty(item => item.ProcessingUntil, DateTime.UtcNow.AddSeconds(options.ProcessingLeaseSeconds)), cancellationToken);
        if (claimed != 1)
        {
            throw new InvalidOperationException("Помощник ещё обрабатывает предыдущее сообщение. Дождитесь ответа.");
        }

        return token;
    }

    private Task ReleaseConversationAsync(Guid id, Guid token) => context.Set<AssistantConversation>()
        .Where(item => item.Id == id && item.ProcessingToken == token)
        .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ProcessingToken, (Guid?)null)
            .SetProperty(item => item.ProcessingUntil, (DateTime?)null), CancellationToken.None);

    private Task InvalidateDraftsAsync(Guid conversationId, CancellationToken cancellationToken) => context.Set<AssistantDraft>()
        .Where(item => item.ConversationId == conversationId && item.Status == "pending")
        .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Status, "superseded"), cancellationToken);

    private async Task<AiPilotConversationSnapshot> RestoreAsync(AssistantActor actor, AssistantConversation conversation, CancellationToken cancellationToken)
    {
        var messages = await context.Set<AssistantMessage>().AsNoTracking().Where(item => item.ConversationId == conversation.Id)
            .OrderBy(item => item.Sequence).Select(item => new AiPilotConversationMessage(item.Role, item.Text)).ToArrayAsync(cancellationToken);
        var snapshot = conversation.EngineStateJson == "{}"
            ? new AiPilotConversationSnapshot(messages, 0, null, null, null)
            : JsonSerializer.Deserialize<AiPilotConversationSnapshot>(conversation.EngineStateJson, s_json)! with { Messages = messages };
        engine.Restore(actor.UserId, actor.OrganizationId, conversation.Id, snapshot);

        return snapshot;
    }

    private async Task PersistAsync(AssistantConversation conversation, AiPilotConversationSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var existing = await context.Set<AssistantMessage>().Where(item => item.ConversationId == conversation.Id)
            .OrderBy(item => item.Sequence).ToListAsync(cancellationToken);
        var common = 0;
        while (common < existing.Count && common < snapshot.Messages.Count
            && existing[common].Role == snapshot.Messages[common].Role && existing[common].Text == snapshot.Messages[common].Content)
        {
            common++;
        }

        context.RemoveRange(existing.Skip(common));
        for (var index = common; index < snapshot.Messages.Count; index++)
        {
            var message = snapshot.Messages[index];
            context.Add(new AssistantMessage { ConversationId = conversation.Id, Sequence = index, Role = message.Role, Text = message.Content });
        }

        conversation.EngineStateJson = JsonSerializer.Serialize(snapshot with { Messages = [] }, s_json);
        conversation.ExpiresAt = DateTime.UtcNow.AddMinutes(options.ConversationTtlMinutes);
        conversation.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task TryAppendExecutionFeedbackAsync(AssistantActor actor, Guid conversationId, string message, bool succeeded)
    {
        try
        {
            var conversation = await this.GetConversationAsync(actor, conversationId, CancellationToken.None);
            await this.RestoreAsync(actor, conversation, CancellationToken.None);
            await engine.AddFeedbackAsync(actor.UserId, actor.OrganizationId, conversationId, succeeded ? "completed" : "failed",
                message[..Math.Min(message.Length, options.MaximumMessageCharacters - 100)], CancellationToken.None);
            await this.PersistAsync(conversation, engine.Export(actor.UserId, actor.OrganizationId, conversationId), CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Не удалось дополнить диалог результатом команды {ConversationId}; результат команды сохранён отдельно.", conversationId);
        }
    }

    private async Task<TimeZoneInfo> GetTimeZoneAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var value = await context.Organizations.AsNoTracking().Where(item => item.Id == organizationId)
            .Select(item => item.ShiftTimeZoneId).SingleAsync(cancellationToken);

        return string.IsNullOrWhiteSpace(value) ? TimeZoneInfo.FindSystemTimeZoneById(options.DefaultTimeZone) : TimeZoneInfo.FindSystemTimeZoneById(value);
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
