using System.Text.Json;
using OpenCRM.WebApp.Infrastructure.AiPilot;

namespace OpenCRM.WebApp.Infrastructure.Assistant;

public sealed record AssistantDraftPreview(
    Guid Id,
    AiPilotCommand Command,
    string Summary,
    string Target,
    IReadOnlyList<string> Changes,
    bool Destructive,
    bool ReadOnly,
    JsonElement? Data,
    IReadOnlyList<string> Warnings,
    decimal? Cost);

public sealed record AssistantClarification(string Field, string Message, IReadOnlyList<AssistantChoice> Choices);

public sealed record AssistantReply(
    Guid ConversationId,
    string Message,
    bool AwaitingAnswer,
    AssistantDraftPreview? Draft = null,
    AssistantClarification? Clarification = null,
    decimal? Cost = null,
    Guid? MessageId = null);

public sealed record AssistantSpeechOutput(byte[] Content, string ContentType);

public sealed class AssistantConversationSelection
{
    public Guid? MaintenanceId { get; set; }
    public Dictionary<string, string> Choices { get; set; } = new(StringComparer.Ordinal);
    public AssistantClarification? PendingChoice { get; set; }

    public AssistantActionSelection ToActionSelection() => new(this.MaintenanceId, this.Choices);
}
