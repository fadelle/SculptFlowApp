namespace Omni.App.Entities.Requests.Members;

/// <summary>Body for POST /api/members/invites: who to invite (the name is optional; the invitee confirms or types it).</summary>
public record InviteMemberRequest(string? Email, string? FullName);
