namespace Omni.App.Entities.Responses.Members;

/// <summary>One person with access to a client (a client_users membership + their user). Role is client or member;
/// IsActive = the membership and the user are both active. Never includes anything credential-related.</summary>
public record MemberResponse(
    string UserId,
    string? FullName,
    string? Email,
    string Role,
    bool IsActive,
    DateTimeOffset JoinedAt,
    DateTimeOffset? LastLoginAt
);
