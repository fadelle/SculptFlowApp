namespace Omni.App.Entities.Requests.Members;

/// <summary>Body for POST /api/members: a new member user of the caller's client, signing in with this password.</summary>
public record AddMemberRequest(string? FullName, string? Email, string? Password);
