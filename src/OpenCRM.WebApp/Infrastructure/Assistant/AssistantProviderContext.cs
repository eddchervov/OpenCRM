using OpenCRM.Domain.Assistant;
using OpenCRM.WebApp.Infrastructure.AiPilot;

namespace OpenCRM.WebApp.Infrastructure.Assistant;

public sealed class AssistantProviderContext
{
    public AssistantActor? Actor { get; set; }
    public Guid? ConversationId { get; set; }
    public AssistantSettings? Settings { get; set; }
    public IReadOnlyList<AiPilotActionDefinition>? Actions { get; set; }
}
