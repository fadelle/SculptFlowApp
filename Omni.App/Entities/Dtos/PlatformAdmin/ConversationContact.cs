namespace Omni.App.Entities.Dtos.PlatformAdmin;

/// <summary>The contact a conversation belongs to, by name.</summary>
public record ConversationContact(Guid Id, string? FullName, string? FirstName, string? LastName, string? Phone);
