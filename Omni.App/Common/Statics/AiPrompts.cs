namespace Omni.App.Common.Statics;

/// <summary>
/// The AI prompt files in Resources/ai (embedded as "ai/{file}", read with AiPromptTemplate). They are extracted from the
/// n8n workflows by tools/extract-n8n-prompts.ps1, never edited by hand; AiPromptTemplateTests checks them against the
/// workflow JSON. Placeholders are {{name}}, filled by AiPromptValues.
/// </summary>
public static class AiPrompts
{
    /// <summary>The bot agent's system prompt (n8n "AI Agent" node).</summary>
    public const string AgentSystem = "agent-system.txt";

    /// <summary>The result formatter's system prompt (n8n "result_formatter" node).</summary>
    public const string FormatterSystem = "formatter-system.txt";

    /// <summary>The result formatter's user message: the verified result and the latest contact message.</summary>
    public const string FormatterUser = "formatter-user.txt";

    /// <summary>The retrieval benchmark question generator's system prompt (no placeholders).</summary>
    public const string BenchmarkSystem = "benchmark-system.txt";

    /// <summary>The AI tools the agent is offered (n8n tool nodes), by the name the model sees.</summary>
    public static readonly IReadOnlyList<string> ToolNames =
    [
        "search_knowledge", "get_services", "get_available_slots", "get_my_bookings", "update_contact", "update_contact_details",
        "update_stage"
    ];

    /// <summary>The description the model reads for a tool.</summary>
    public static string ToolDescription(string tool) => $"tool.{tool}.txt";

    /// <summary>The JSON schema of the arguments the model fills for a tool.</summary>
    public static string ToolParameters(string tool) => $"tool.{tool}.parameters.json";
}
