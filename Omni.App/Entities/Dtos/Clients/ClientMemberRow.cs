namespace Omni.App.Entities.Dtos.Clients;

/// <summary>One membership of a client with its user. Active = the membership and the user are both active.</summary>
public record ClientMemberRow(string UserId, string? Email, string? FullName, string Role, bool IsActive, DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);
