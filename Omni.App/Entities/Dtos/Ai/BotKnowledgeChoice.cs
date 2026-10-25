namespace Omni.App.Entities.Dtos.Ai;

/// <summary>A Knowledge Base source the bot editor offers: a document (Kind "document", typed or uploaded) or a website
/// source with all its pages (Kind "website").</summary>
public record BotKnowledgeChoice(Guid Id, string Kind, string Title, bool IsActive)
{
    public const string Document = "document";
    public const string Website = "website";
}
