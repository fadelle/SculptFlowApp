using Omni.App.Common.Enums;
using Omni.App.Common.Statics;

namespace Omni.App.Entities.Dtos.Ai;

/// <summary>A bot as the Bots pages show it. Capabilities are the actions it really runs (the template's for a template
/// bot); Instructions are a custom bot's own (CoreInstructions gives a template's locked ones). AccountCount = connected
/// accounts it is assigned to.</summary>
public record BotDto(
    Guid Id,
    string Name,
    string? Description,
    string Template,
    string? Instructions,
    IReadOnlyList<string> Capabilities,
    string KnowledgeScope,
    IReadOnlyList<Guid> KnowledgeDocumentIds,
    IReadOnlyList<Guid> WebsiteSourceIds,
    string? Language,
    string HandoffPolicy,
    string? HandoffMessage,
    string Status,
    int AccountCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public bool IsTemplate => Template != BotTemplate.Custom;

    /// <summary>A template bot's locked core instructions; null for a custom bot.</summary>
    public string? CoreInstructions => BotTemplates.For(Template)?.CoreInstructions;
}
