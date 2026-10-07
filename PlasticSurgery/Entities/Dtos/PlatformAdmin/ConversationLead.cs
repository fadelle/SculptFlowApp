namespace PlasticSurgery.Entities.Dtos.PlatformAdmin;

/// <summary>The lead a conversation belongs to, by name.</summary>
public record ConversationLead(Guid Id, string? FullName, string? FirstName, string? LastName, string? Phone);
