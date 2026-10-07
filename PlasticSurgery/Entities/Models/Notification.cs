namespace PlasticSurgery.Entities.Models;

/// <summary>One row in a clinic's notification center (the bell). Created ONLY from a confirmed backend event —
/// see NotificationType and Business/Contracts/Services/Notifications/INotificationService.cs for the full "when" list. Never created from an
/// AI intention alone (e.g. the AI wanting to hand off, or wanting to book, isn't enough — the mode change or
/// the appointment row has to actually exist first).</summary>
public class Notification
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Message { get; set; }
    public Guid? LeadId { get; set; }
    public Guid? ConversationId { get; set; }
    public Guid? AppointmentId { get; set; }
    public Guid? ChannelIntegrationId { get; set; }

    /// <summary>Relative URL the bell UI navigates to on click — precomputed server-side (e.g. "/inbox?conversationId=..."
    /// or "/dashboard/appointments/{id}") so the UI never has to know which entity type maps to which page.</summary>
    public string? Link { get; set; }

    public bool IsRead { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
