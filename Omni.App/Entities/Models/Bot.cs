namespace Omni.App.Entities.Models;

/// <summary>An AI bot a client configured (crm.bots): how it behaves. Where and when it answers is set per connected account
/// (crm.inbox_settings.bot_id). A template bot (Template support/booking/sales) runs the template's locked instructions and
/// actions (Common/Statics/BotTemplates), so Instructions and Capabilities are used only by a custom bot. Read and changed
/// through Business/Services/Ai/BotService.</summary>
public class Bot
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }

    public string Name { get; set; } = "";
    public string? Description { get; set; }

    /// <summary>BotTemplate: support, booking, sales or custom.</summary>
    public string Template { get; set; } = Common.Enums.BotTemplate.Custom;

    /// <summary>A custom bot's own instructions (null for a template bot).</summary>
    public string? Instructions { get; set; }

    /// <summary>A custom bot's allowed actions (BotCapability values); empty for a template bot.</summary>
    public List<string> Capabilities { get; set; } = [];

    /// <summary>BotKnowledgeScope: all sources, or only KnowledgeSources.</summary>
    public string KnowledgeScope { get; set; } = Common.Enums.BotKnowledgeScope.All;

    /// <summary>A fixed reply language (two-letter code); null = the contact's own language (automatic).</summary>
    public string? Language { get; set; }

    /// <summary>BotHandoffPolicy.</summary>
    public string HandoffPolicy { get; set; } = Common.Enums.BotHandoffPolicy.OnRequestOrUnsure;

    /// <summary>Sent to the contact once each time the bot hands the conversation to the team; null = none.</summary>
    public string? HandoffMessage { get; set; }

    /// <summary>BotStatus: draft, active or inactive.</summary>
    public string Status { get; set; } = Common.Enums.BotStatus.Draft;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>The selected sources when KnowledgeScope is "selected".</summary>
    public List<BotKnowledgeSource> KnowledgeSources { get; set; } = [];
}
