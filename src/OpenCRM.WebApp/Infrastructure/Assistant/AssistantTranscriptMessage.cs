namespace OpenCRM.WebApp.Infrastructure.Assistant;

public sealed record AssistantTranscriptMessage(Guid Id, string Role, string Text, DateTime CreatedAt, int Sequence);
