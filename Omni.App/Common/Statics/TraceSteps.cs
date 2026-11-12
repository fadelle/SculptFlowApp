namespace Omni.App.Common.Statics;

/// <summary>
/// The step names stored in activity.message_trace_steps.step. Grouped by area: webhook in, inbox, bot (n8n), sending.
/// Delivery statuses, billing and business events are not steps (the trace view reads their own tables).
/// </summary>
public static class TraceSteps
{
    // Webhooks (Meta, Telegram)
    public const string WebhookReceived = "webhook.received";
    public const string WebhookRejected = "webhook.rejected";
    public const string WebhookClientUnresolved = "webhook.client_unresolved";
    public const string WebhookIgnored = "webhook.ignored";
    /// <summary>One event of a delivery failed and was skipped; the rest of the delivery went on (QA t10-03).</summary>
    public const string WebhookEventFailed = "webhook.event_failed";

    // Inbox
    public const string MessageSaved = "message.saved";
    public const string MessageDeduplicated = "message.deduplicated";
    public const string ConversationReopened = "conversation.reopened";
    public const string ConversationModeChanged = "conversation.mode_changed";
    public const string InboxNotified = "inbox.notified";
    public const string AutoReplySkipped = "inbox.auto_reply_skipped";
    public const string HandoffMessageSkipped = "inbox.handoff_message_skipped";

    // Bot (BotTrigger, BotRunWorker, BotEngine)
    public const string BotSkipped = "bot.skipped";
    public const string BotTriggered = "bot.triggered";
    public const string BotTriggerFailed = "bot.trigger_failed";
    public const string BotToolCalled = "bot.tool_called";
    public const string BotRunStarted = "bot.run_started";
    public const string BotModelCalled = "bot.model_called";
    public const string BotDecision = "bot.decision";
    public const string BotActionResult = "bot.action_result";
    public const string BotReplyStopped = "bot.reply_stopped";
    public const string BotRunFailed = "bot.run_failed";
    /// <summary>A waiting bot run the app stopped before it could start (a deploy or restart; QA t7-05): the contact gets no reply.</summary>
    public const string BotRunDropped = "bot.run_dropped";
    /// <summary>The bot was asked and nothing came back within Ai / NoAnswerHandoffMinutes; the conversation went to the team (QA t9-04).</summary>
    public const string BotNoAnswer = "bot.no_answer";
    /// <summary>Several waiting text messages of one conversation answered in one run (QA t4-ai-18).</summary>
    public const string BotRunsMerged = "bot.runs_merged";

    // Contacts (from the bot's tools)
    public const string ContactLanguageIgnored = "contact.language_ignored";

    // Sending
    public const string SendRequested = "send.requested";
    public const string SendBlocked = "send.blocked";
    public const string SendRejectedMode = "send.rejected_mode";
    public const string SendProviderAccepted = "send.provider_accepted";
    public const string SendProviderFailed = "send.provider_failed";
}