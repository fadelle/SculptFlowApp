namespace PlasticSurgery.Dtos;

/// <summary>One person with access to a clinic (a clinic_users membership + their Identity account).
/// Never includes anything credential-related.</summary>
public record StaffMemberResponse(
    string UserId,
    string? FullName,
    string? Email,
    bool IsActive,
    DateTimeOffset JoinedAt
);
