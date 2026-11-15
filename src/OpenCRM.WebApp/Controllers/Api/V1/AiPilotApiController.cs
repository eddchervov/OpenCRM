using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain.Assistant;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.WebApp.Infrastructure.AiPilot;
using OpenCRM.WebApp.Infrastructure.Assistant;

namespace OpenCRM.WebApp.Controllers.Api.V1;

/// <summary> Единый серверный помощник сайта; модели не передаются учётные данные приложения. </summary>
[ApiController]
[EnableRateLimiting("assistant")]
[Authorize]
[Route("api/v1/assistant")]
[Route("api/v1/ai-pilot")]
public sealed class AiPilotApiController(
    IConfiguration configuration,
    UserManager<ApplicationUser> users,
    IOrganizationQueryService organizations,
    AssistantAccessService access,
    AssistantService assistant,
    OpenCRMContext context,
    AssistantOptions options) : ControllerBase
{
    [HttpGet("availability")]
    public async Task<IActionResult> Availability(Guid organizationId, CancellationToken cancellationToken)
    {
        try
        {
            await access.EnsureAvailableAsync(this.Actor(organizationId), cancellationToken);
            return this.Ok(new { visible = true, enabled = true, organizationId, pollingSeconds = options.AvailabilityPollingSeconds, url = $"{this.Request.PathBase}/assistant/{organizationId:D}" });
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException)
        {
            return this.Ok(new { visible = false, enabled = false, organizationId, pollingSeconds = options.AvailabilityPollingSeconds });
        }
    }

    [HttpGet("context")]
    public async Task<IActionResult> GetContext(Guid? organizationId, CancellationToken cancellationToken)
    {
        var actor = this.Actor(this.ResolveOrganization(organizationId));
        var settings = await access.EnsureAvailableAsync(actor, cancellationToken);
        var organization = await organizations.GetByIdAsync(actor.UserId, actor.OrganizationId);
        var grant = await context.Set<AssistantOrganizationAccess>().AsNoTracking()
            .SingleAsync(item => item.OrganizationId == actor.OrganizationId, cancellationToken);
        return this.Ok(new
        {
            actor.OrganizationId,
            organizationName = organization.Name,
            actor.UserId,
            workspaceUrl = this.WorkspaceUrl(organization),
            actions = await access.GetActionsAsync(actor, cancellationToken),
            textModel = settings.TextModel,
            speechModel = settings.SpeechModel,
            serverSpeechEnabled = settings.SpeechRepliesEnabled,
            telegramAvailable = settings.TelegramEnabled && grant.TelegramEnabled,
            telegramConfigured = AssistantConfiguration.IsTelegramConfigured(configuration),
            limits = new { options.MaximumMessageCharacters, options.MaximumAudioBytes, options.MaximumAudioDurationSeconds, options.AvailabilityPollingSeconds }
        });
    }

    [HttpPost("chat")]
    public async Task<AssistantReply> Chat([FromBody] AssistantChatRequest request, CancellationToken cancellationToken) =>
        await assistant.ChatAsync(this.Actor(this.ResolveOrganization(request.OrganizationId)), request.Text, request.ConversationId, cancellationToken);

    [HttpGet("conversations/{id:guid}")]
    public async Task<IActionResult> Conversation(Guid id, Guid organizationId, CancellationToken cancellationToken)
    {
        var actor = this.Actor(organizationId);
        await access.EnsureAvailableAsync(actor, cancellationToken);
        var conversation = await context.Set<AssistantConversation>().AsNoTracking().SingleOrDefaultAsync(item => item.Id == id &&
            item.UserId == actor.UserId && item.OrganizationId == actor.OrganizationId && item.Channel == actor.Channel &&
            !item.IsClosed && item.ExpiresAt > DateTime.UtcNow, cancellationToken);
        if (conversation is null)
        {
            return this.NotFound();
        }

        var selection = JsonSerializer.Deserialize<AssistantConversationSelection>(conversation.SelectionJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var draft = await context.Set<AssistantDraft>().AsNoTracking().Where(item => item.ConversationId == id && item.Status == "pending" && item.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(item => item.CreatedAt).Select(item => item.PreviewJson).FirstOrDefaultAsync(cancellationToken);
        var messages = await context.Set<AssistantMessage>().AsNoTracking().Where(item => item.ConversationId == id)
            .OrderBy(item => item.Sequence).ToListAsync(cancellationToken);

        return this.Ok(new
        {
            conversationId = id,
            messages = AssistantDashboardService.VisibleMessages(messages),
            draft = draft is null ? null : JsonSerializer.Deserialize<AssistantDraftPreview>(draft, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            clarification = selection?.PendingChoice
        });
    }

    [HttpPost("choose")]
    public async Task<AssistantReply> Choose([FromBody] AssistantChooseRequest request, CancellationToken cancellationToken) =>
        await assistant.ChooseAsync(this.Actor(this.ResolveOrganization(request.OrganizationId)), request.ConversationId,
            request.Field, request.Value, cancellationToken);

    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm([FromBody] AssistantConfirmRequest request, CancellationToken cancellationToken)
    {
        var actor = this.Actor(this.ResolveOrganization(request.OrganizationId));
        var result = await assistant.ConfirmAsync(actor, request.DraftId, request.Confirmed, cancellationToken);
        var organization = await organizations.GetByIdAsync(actor.UserId, actor.OrganizationId);
        var workspace = this.WorkspaceUrl(organization);
        return this.Ok(new
        {
            result.Message, result.MaintenanceId, result.VehicleId, result.ClientId, result.Url,
            maintenanceUrl = result.MaintenanceId.HasValue ? $"{workspace}#maintenance/{result.MaintenanceId:D}" : null,
            vehicleUrl = result.VehicleId.HasValue ? $"{workspace}#vehicles/{result.VehicleId:D}" : null,
            clientUrl = result.ClientId.HasValue ? $"{workspace}#clients/{result.ClientId:D}" : null
        });
    }

    [HttpPost("close")]
    public async Task<IActionResult> Close([FromBody] AssistantCloseRequest request, CancellationToken cancellationToken)
    {
        await assistant.CloseAsync(this.Actor(this.ResolveOrganization(request.OrganizationId)), request.ConversationId, cancellationToken);
        return this.NoContent();
    }

    [HttpPost("transcribe")]
    [ServiceFilter(typeof(AssistantUploadLimitFilter))]
    public async Task<IActionResult> Transcribe(IFormFile audio, Guid? organizationId, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(audio.FileName).ToLowerInvariant();
        if (audio.Length <= 0 || audio.Length > options.MaximumAudioBytes ||
            !new[] { ".webm", ".mp3", ".m4a", ".wav", ".ogg", ".mp4", ".flac" }.Contains(extension))
        {
            return this.BadRequest(new { message = "Размер или формат аудиофайла не соответствует ограничениям помощника." });
        }

        await using var stream = audio.OpenReadStream();
        var result = await assistant.TranscribeAsync(this.Actor(this.ResolveOrganization(organizationId)), stream,
            "voice" + extension, audio.ContentType, cancellationToken);
        return this.Ok(result);
    }

    [HttpPost("speech")]
    public async Task<IActionResult> Speech([FromBody] AssistantSpeechRequest request, CancellationToken cancellationToken)
    {
        var actor = this.Actor(this.ResolveOrganization(request.OrganizationId));
        string? text = null;
        Guid? conversationId = null;
        if (request.MessageId.HasValue)
        {
            var message = await context.Set<AssistantMessage>().AsNoTracking().Where(item => item.Id == request.MessageId && item.Role == "assistant"
                && item.Conversation.UserId == actor.UserId && item.Conversation.OrganizationId == actor.OrganizationId
                && item.Conversation.Channel == "web").Select(item => new { item.Text, item.ConversationId }).SingleOrDefaultAsync(cancellationToken);
            text = message?.Text;
            conversationId = message?.ConversationId;
        }
        else if (request.DraftId.HasValue)
        {
            var draft = await context.Set<AssistantDraft>().AsNoTracking().SingleOrDefaultAsync(item => item.Id == request.DraftId
                && item.UserId == actor.UserId && item.OrganizationId == actor.OrganizationId && item.Channel == "web" && item.Status == "completed", cancellationToken);
            if (draft is not null)
            {
                text = JsonSerializer.Deserialize<AssistantActionResult>(draft.ResultJson!, new JsonSerializerOptions(JsonSerializerDefaults.Web))?.Message;
                conversationId = draft.ConversationId;
            }
        }

        if (string.IsNullOrWhiteSpace(text)) return this.NotFound();
        var audio = await assistant.SpeakAsync(actor, text, cancellationToken, conversationId);
        this.Response.Headers.CacheControl = "no-store";
        return this.File(audio.Content, audio.ContentType);
    }

    private AssistantActor Actor(Guid organizationId) => new(
        users.GetUserId(this.User) ?? throw new UnauthorizedAccessException("Сессия завершилась."), organizationId, "web");

    private Guid ResolveOrganization(Guid? organizationId)
    {
        if (organizationId.HasValue && organizationId != Guid.Empty) return organizationId.Value;
        // Организация задаётся явно; подстановка организации демонстрационного стенда запрещена.
        throw new InvalidOperationException("Выберите организацию для помощника.");
    }

    private string WorkspaceUrl(OpenCRM.Modules.Organizations.Contracts.Dtos.OrganizationDto organization) =>
        $"{this.Request.PathBase}/{(organization.PrimaryScenarioCode == "detailing" ? "detailing" : "autoservice")}/{organization.Id:D}";
}

public sealed record AssistantChatRequest(string Text, Guid? ConversationId = null, Guid? OrganizationId = null);
public sealed record AssistantChooseRequest(Guid ConversationId, string Field, string Value, Guid? OrganizationId = null);
public sealed record AssistantConfirmRequest(Guid DraftId, bool Confirmed = false, Guid? OrganizationId = null);
public sealed record AssistantCloseRequest(Guid ConversationId, Guid? OrganizationId = null);
public sealed record AssistantSpeechRequest(Guid? MessageId = null, Guid? DraftId = null, Guid? OrganizationId = null);
