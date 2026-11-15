using OpenCRM.WebApp.Infrastructure.AiPilot;

namespace OpenCRM.WebApp.Infrastructure.Assistant;

public sealed class AssistantActionExecutor(
    IEnumerable<IAssistantActionHandler> handlers,
    IAssistantExecutionGuard guard) : IAssistantActionExecutor
{
    public async Task<AssistantPreparedAction> PrepareAsync(
        AiPilotCommand command,
        AssistantActor actor,
        AssistantActionSelection selection,
        CancellationToken cancellationToken)
    {
        if (!RouterAiPilotClient.IsValidConversationCommand(command, true) ||
            !AiPilotActionCatalog.TryGet(command.Action, out var definition) ||
            !AiPilotActionCatalog.HasValidParameters(new AiPilotActionCommand(definition, command.Parameters)))
        {
            throw new InvalidOperationException("Команда не прошла проверку разрешённых полей.");
        }

        await guard.EnsureAllowedAsync(actor, command.Action, cancellationToken);
        var matches = handlers.Where(handler => handler.Actions.Contains(command.Action, StringComparer.Ordinal)).ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidOperationException("Для этого действия помощника не настроен серверный обработчик.");
        }

        var plan = await matches[0].PrepareAsync(command, actor, selection, cancellationToken);
        if (string.IsNullOrWhiteSpace(plan.Target) || string.IsNullOrWhiteSpace(plan.Snapshot) || plan.Snapshot.Length > 1_000_000)
        {
            throw new InvalidOperationException("Не удалось безопасно подготовить действие.");
        }

        return plan;
    }
}
