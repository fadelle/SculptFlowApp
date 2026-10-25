namespace Omni.App.Entities.Requests.Ai;

/// <summary>The bot editor's form. Instructions and Capabilities are used only for a custom bot (a template bot's are locked).
/// KnowledgeScope "selected" needs at least one KnowledgeDocumentIds/WebsiteSourceIds entry. Language null = automatic.</summary>
public record SaveBotRequest(
    string? Name,
    string? Description,
    string? Instructions,
    IReadOnlyList<string>? Capabilities,
    string? KnowledgeScope,
    IReadOnlyList<Guid>? KnowledgeDocumentIds,
    IReadOnlyList<Guid>? WebsiteSourceIds,
    string? Language,
    string? HandoffPolicy,
    string? HandoffMessage,
    string? Status);
