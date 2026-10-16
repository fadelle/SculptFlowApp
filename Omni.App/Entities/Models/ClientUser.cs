namespace Omni.App.Entities.Models;

/// <summary>
/// Links a logged-in Identity user (IdentityUser.Id, string) to the client they belong to — nothing
/// more. Deliberately no role/permission fields per the MVP scope decision: every linked user has
/// full access to their client's data, there is no owner/manager/receptionist/surgeon distinction.
/// MVP assumes one active membership per user (enforced by UNIQUE(client_id, user_id), not by a
/// separate "is this the only active row" check) — see CurrentClientContext, the one place that
/// reads this table to resolve "the current client" for every dashboard page/API.
/// </summary>
public class ClientUser
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }

    /// <summary>IdentityUser.Id — a string (Identity's default key type), not a Guid.</summary>
    public string UserId { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Client? Client { get; set; }
}
