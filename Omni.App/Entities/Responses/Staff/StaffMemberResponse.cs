namespace Omni.App.Entities.Responses.Staff;

/// <summary>One person with access to a client (a client_users membership + their Identity account).
/// Never includes anything credential-related.</summary>
public record StaffMemberResponse(
    string UserId,
    string? FullName,
    string? Email,
    bool IsActive,
    DateTimeOffset JoinedAt
);
