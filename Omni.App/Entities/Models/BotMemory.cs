namespace Omni.App.Entities.Models;

/// <summary>One exchange the AI bot remembers (crm.bot_memory): the contact's message and the agent's raw final output, like
/// one turn of n8n's Simple Memory. Belongs to a live conversation (ConversationId) or a bot test conversation
/// (TestSessionId), never both; BotEngine keeps the newest Ai / MemoryExchanges.</summary>
public class BotMemory
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Guid? ConversationId { get; set; }
    public Guid? TestSessionId { get; set; }

    /// <summary>The contact's message text (the agent's input).</summary>
    public string UserText { get; set; } = "";

    /// <summary>The agent's final output exactly as the model returned it (the decision JSON).</summary>
    public string Output { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }
}
