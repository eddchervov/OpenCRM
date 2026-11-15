using System.Text.Json;
using OpenCRM.WebApp.Infrastructure.AiPilot;

namespace OpenCRM.WebApp.Infrastructure.Assistant;

public sealed record AssistantActor(string UserId, Guid OrganizationId, string Channel);

public sealed record AssistantActionSelection(
    Guid? MaintenanceId = null,
    IReadOnlyDictionary<string, string>? CatalogChoices = null);

public sealed record AssistantChoice(string Value, string Label);

public sealed class AssistantClarificationException(
    string message,
    string field,
    IReadOnlyList<AssistantChoice>? choices = null) : InvalidOperationException(message)
{
    public string Field { get; } = field;
    public IReadOnlyList<AssistantChoice> Choices { get; } = choices ?? [];
}

public sealed record AssistantActionResult(
    string Message,
    Guid? MaintenanceId = null,
    Guid? VehicleId = null,
    Guid? ClientId = null,
    string? Url = null);

public sealed class AssistantPreparedAction
{
    public required string Target { get; init; }
    public required IReadOnlyList<string> Changes { get; init; }
    public required string Snapshot { get; init; }
    public bool Destructive { get; init; }
    public bool ReadOnly { get; init; }
    public JsonElement? Data { get; init; }
    public required Func<CancellationToken, Task<AssistantActionResult>> ExecuteAsync { get; init; }
}

public interface IAssistantActionExecutor
{
    Task<AssistantPreparedAction> PrepareAsync(
        AiPilotCommand command,
        AssistantActor actor,
        AssistantActionSelection selection,
        CancellationToken cancellationToken);
}

public interface IAssistantActionHandler : IAssistantActionExecutor
{
    IReadOnlyCollection<string> Actions { get; }
}

public interface IAssistantExecutionGuard
{
    Task EnsureAllowedAsync(AssistantActor actor, string action, CancellationToken cancellationToken);
}
