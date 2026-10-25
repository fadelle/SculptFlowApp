namespace Omni.App.Common.Enums;

/// <summary>crm.bots.knowledge_scope: which Knowledge Base sources the bot's search_knowledge sees.</summary>
public static class BotKnowledgeScope
{
    /// <summary>Every active source.</summary>
    public const string All = "all";

    /// <summary>Only the sources in crm.bot_knowledge_sources.</summary>
    public const string Selected = "selected";

    public static readonly IReadOnlySet<string> Values = new HashSet<string>(StringComparer.Ordinal) { All, Selected };
}
