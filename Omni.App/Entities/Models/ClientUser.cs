namespace Omni.App.Entities.Models;

/// <summary>
/// Links a client or member user (AppUser.Id, string) to the client they work in (identity.client_users). Admin
/// users have none. A user has at most one ACTIVE membership: a rule MemberService and MemberAdminService check before
/// activating one (not a unique index, so agencies with several clients can come later). CurrentClientContext reads
/// the active row on every signed-in request to resolve "the current client".
/// </summary>
public class ClientUser
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }

    /// <summary>AppUser.Id — a string (Identity's default key type), not a Guid.</summary>
    public string UserId { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Client? Client { get; set; }
}
