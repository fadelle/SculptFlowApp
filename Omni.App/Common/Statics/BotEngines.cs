namespace Omni.App.Common.Statics;

/// <summary>The values of the Ai / BotEngine setting: which engine answers contacts and the bot test panel.</summary>
public static class BotEngines
{
    /// <summary>The n8n AI assistant workflow (AiTriggerNotifier posts to N8n:AiWebhookUrl).</summary>
    public const string N8n = "n8n";

    /// <summary>BotEngine in the app (BotRunQueue, BotRunWorker).</summary>
    public const string Code = "code";
}
