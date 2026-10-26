namespace Omni.App.Common.Statics;

/// <summary>The values of the n8n-or-code switches: Ai / BotEngine (the AI bot), Calendars / SyncEngine (calendar sync) and
/// Benchmark / GeneratorEngine (benchmark questions). Default n8n until each code path is tested.</summary>
public static class WorkflowEngines
{
    /// <summary>The n8n workflow (AiTriggerNotifier, CalendarSyncNotifier, N8nKnowledgeBenchmarkGeneratorClient).</summary>
    public const string N8n = "n8n";

    /// <summary>The same workflow as code in the app (BotEngine, CalendarSyncClient, KnowledgeBenchmarkGenerator).</summary>
    public const string Code = "code";
}
