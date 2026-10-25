namespace Omni.App.Common.Statics;

/// <summary>Common event_type values used across the platform (not DB-enforced — events.event_type is free text).
/// Named EventTypes (not EventType) so it can't collide with EventLog.EventType the way
/// Contact.Qualification originally did with a same-named class — see that fix's comment.</summary>
public static class EventTypes
{
    public const string ContactCreated = "contact_created";
    /// <summary>A manual (dashboard) or AI stage change whose new stage's category has no event of its own; metadata
    /// {"from","to"} holds the stage keys.</summary>
    public const string ContactStageChanged = "contact_stage_changed";
    /// <summary>A contact opted out of business-started messages (a stop word, or a member on the contact page).</summary>
    public const string ContactOptedOut = "contact_opted_out";
    /// <summary>A contact opted back in (a start word, or a member on the contact page).</summary>
    public const string ContactOptedIn = "contact_opted_in";
    /// <summary>A contact and its data were deleted for a privacy request (the event keeps no link to the contact).</summary>
    public const string ContactDeleted = "contact_deleted";
    public const string BookingCreated = "booking_created";
    public const string BookingAttended = "booking_attended";
    public const string BookingNoShow = "booking_no_show";
    public const string HumanHandoff = "human_handoff";
    public const string OrderConfirmed = "order_confirmed";
    public const string MessageFailed = "message_failed";
    public const string FollowupScheduled = "followup_scheduled";

    // Inbox / WhatsApp messaging events — see MessageService and ConversationService.
    public const string ReturnedToAi = "returned_to_ai";
    public const string ConversationClosed = "conversation_closed";
    public const string ConversationReopened = "conversation_reopened";
    public const string ContactMessageReceived = "contact_message_received";
    public const string MemberMessageSentDashboard = "member_message_sent_dashboard";
    public const string MemberMessageSentWhatsAppApp = "member_message_sent_whatsapp_app";
    public const string AiMessageSent = "ai_message_sent";
    public const string CampaignMessageSent = "campaign_message_sent";
    /// <summary>A booking reminder went out (BookingReminderWorker).</summary>
    public const string BookingReminderSent = "booking_reminder_sent";
    /// <summary>A channel's automatic reply went out; metadata {"trigger": "new" | "reopened"} (AutoReplyTrigger).</summary>
    public const string AutoReplySent = "auto_reply_sent";
    /// <summary>The bot's handoff message went out after it handed the conversation to the team; metadata {"botId": ...}.</summary>
    public const string HandoffMessageSent = "handoff_message_sent";

    // AI assistant tool surface — see Controllers/Integrations/AiController.cs.
    public const string BookingRescheduled = "booking_rescheduled";
    public const string BookingCanceled = "booking_canceled";

    // WhatsApp webhook event routing (n8n → WhatsAppIntegrationEventsController).
    public const string WhatsAppTemplateStatusChanged = "whatsapp_template_status_changed";
    public const string WhatsAppHealthEventReceived = "whatsapp_health_event_received";
    public const string WhatsAppUnknownEventReceived = "whatsapp_unknown_event_received";
    public const string WhatsAppHistorySyncReceived = "whatsapp_history_sync_received";
    public const string WhatsAppAppStateSyncReceived = "whatsapp_app_state_sync_received";
}
