namespace Omni.App.Common.Enums;

public static class NotificationType
{
    public const string NewContact = "NEW_CONTACT";
    public const string Handoff = "HANDOFF";
    public const string BookingCreated = "BOOKING_CREATED";
    public const string BookingRescheduled = "BOOKING_RESCHEDULED";
    public const string BookingCanceled = "BOOKING_CANCELED";
    public const string CampaignReply = "CAMPAIGN_REPLY";
    public const string OutboundMessageFailed = "OUTBOUND_MESSAGE_FAILED";
    public const string IntegrationUnhealthy = "INTEGRATION_UNHEALTHY";
    /// <summary>Meta approved, rejected, paused, disabled, flagged or re-categorised a WhatsApp template.</summary>
    public const string TemplateStatusChanged = "TEMPLATE_STATUS_CHANGED";
    /// <summary>Something about the client's billing they should know, e.g. add-ons that ended at a renewal.</summary>
    public const string Billing = "BILLING";
}
