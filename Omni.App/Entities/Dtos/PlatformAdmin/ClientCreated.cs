namespace Omni.App.Entities.Dtos.PlatformAdmin;

/// <summary>What opening a client did (QA t6-admin-20). The client exists whatever the rest says: PlanStarted false = no plan
/// yet (assign one on its Billing page); InviteSent false = the email didn't go (send it again with POST
/// clients/{id}/owner-invite).</summary>
public record ClientCreated(
    Guid ClientId,
    string Slug,
    string? PlanCode,
    bool PlanStarted,
    bool InviteSent,
    DateTimeOffset? InviteExpiresAt
);
