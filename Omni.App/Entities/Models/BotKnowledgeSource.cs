namespace Omni.App.Entities.Models;

/// <summary>One Knowledge Base source a bot may search (crm.bot_knowledge_sources): a document (typed or uploaded) or a
/// whole website source (all of its pages). Exactly one of the two ids is set.</summary>
public class BotKnowledgeSource
{
    public Guid Id { get; set; }
    public Guid BotId { get; set; }
    public Guid? KnowledgeDocumentId { get; set; }
    public Guid? WebsiteSourceId { get; set; }
}
