using Omni.App.Entities.Dtos.Knowledge;

namespace Omni.App.Entities.Dtos.Ai;

/// <summary>The bot behind an AI tool call, checked by BotToolAttribute (IBotRuntimeService.AuthorizeAsync) and handed to the
/// action: whose bot it is, what it may do, which Knowledge Base it searches, and TestSessionId when the call belongs to a
/// test conversation (write actions are then simulated, IBotTestService).</summary>
public record BotRunContext(
    Guid ClientId,
    Guid BotId,
    IReadOnlySet<string> Capabilities,
    KnowledgeSourceFilter? KnowledgeFilter,
    string? HandoffMessage,
    Guid? TestSessionId)
{
    public const string ItemKey = "omni.bot-run";

    public bool IsTest => TestSessionId is not null;

    public bool Allows(string capability) => Capabilities.Contains(capability);
}
