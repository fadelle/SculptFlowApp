namespace PlasticSurgery.Common.Enums;

public static class NotificationType
{
    public const string NewLead = "NEW_LEAD";
    public const string Handoff = "HANDOFF";
    public const string AppointmentBooked = "APPOINTMENT_BOOKED";
    public const string AppointmentRescheduled = "APPOINTMENT_RESCHEDULED";
    public const string AppointmentCancelled = "APPOINTMENT_CANCELLED";
    public const string CampaignReply = "CAMPAIGN_REPLY";
    public const string OutboundMessageFailed = "OUTBOUND_MESSAGE_FAILED";
    public const string IntegrationUnhealthy = "INTEGRATION_UNHEALTHY";
}
