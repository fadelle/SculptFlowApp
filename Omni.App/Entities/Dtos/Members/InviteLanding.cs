namespace Omni.App.Entities.Dtos.Members;

/// <summary>What the accept-invite page shows for a working link: the client's name, the invited email and the name the
/// inviter typed (if any).</summary>
public record InviteLanding(string ClientName, string Email, string? FullName);
