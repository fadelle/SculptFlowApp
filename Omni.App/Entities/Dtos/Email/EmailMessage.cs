namespace Omni.App.Entities.Dtos.Email;

/// <summary>One email to one person: subject, an HTML body and its plain-text twin, and a tag for the provider's
/// statistics ("password_reset", "invite"…). The sender comes from the Email:FromAddress / Email:FromName settings.</summary>
public sealed record EmailMessage(string ToEmail, string? ToName, string Subject, string Html, string Text, string Tag);
