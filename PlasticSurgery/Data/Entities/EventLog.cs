namespace PlasticSurgery.Data.Entities;

/// <summary>Maps to the "events" table. Named EventLog in C# to avoid colliding with the "event" keyword.</summary>
public class EventLog
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }
    public Guid? LeadId { get; set; }
    public Guid? ConversationId { get; set; }
    public Guid? AppointmentId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? Source { get; set; }

    /// <summary>Raw JSON stored in the jsonb "metadata" column. Serialize/deserialize with System.Text.Json as needed.</summary>
    public string Metadata { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Common event_type values used across the platform (not DB-enforced — events.event_type is free text).
/// Named EventTypes (not EventType) so it can't collide with EventLog.EventType the way
/// Lead.QualificationStatus originally did with a same-named class — see that fix's comment.</summary>
public static class EventTypes
{
    public const string LeadCreated = "lead_created";
    public const string LeadContacted = "lead_contacted";
    public const string LeadQualified = "lead_qualified";
    public const string ConsultationBooked = "consultation_booked";
    public const string ConsultationAttended = "consultation_attended";
    public const string ConsultationNoShow = "consultation_no_show";
    public const string HumanHandoff = "human_handoff";
    public const string ProcedureBooked = "procedure_booked";
    public const string MessageFailed = "message_failed";
    public const string FollowupScheduled = "followup_scheduled";

    // Inbox / WhatsApp messaging events — see MessageService and ConversationService.
    public const string ReturnedToAi = "returned_to_ai";
    public const string ConversationClosed = "conversation_closed";
    public const string CustomerMessageReceived = "customer_message_received";
    public const string StaffMessageSentDashboard = "staff_message_sent_dashboard";
    public const string StaffMessageSentWhatsAppApp = "staff_message_sent_whatsapp_app";
    public const string AiMessageSent = "ai_message_sent";
    public const string CampaignMessageSent = "campaign_message_sent";

    // AI agent tool surface — see Controllers/AiController.cs.
    public const string ConsultationRescheduled = "consultation_rescheduled";
    public const string ConsultationCancelled = "consultation_cancelled";

    // WhatsApp webhook event routing (n8n → WhatsAppIntegrationEventsController).
    public const string WhatsAppTemplateStatusChanged = "whatsapp_template_status_changed";
    public const string WhatsAppHealthEventReceived = "whatsapp_health_event_received";
    public const string WhatsAppUnknownEventReceived = "whatsapp_unknown_event_received";
    public const string WhatsAppHistorySyncReceived = "whatsapp_history_sync_received";
    public const string WhatsAppAppStateSyncReceived = "whatsapp_app_state_sync_received";
}
