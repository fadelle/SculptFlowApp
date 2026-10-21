namespace Omni.App.Entities.Responses.Members;

/// <summary>A member invite that was neither accepted nor cancelled. IsExpired: its link no longer works (resend it).
/// Never includes the link or its token.</summary>
public record InviteResponse(Guid Id, string Email, string? FullName, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, bool IsExpired);
