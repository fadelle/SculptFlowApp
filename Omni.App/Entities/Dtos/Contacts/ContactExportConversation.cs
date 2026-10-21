namespace Omni.App.Entities.Dtos.Contacts;

/// <summary>One conversation in a contact export.</summary>
public record ContactExportConversation(Guid Id, string Channel, string Status, DateTimeOffset CreatedAt, DateTimeOffset? LastMessageAt);
