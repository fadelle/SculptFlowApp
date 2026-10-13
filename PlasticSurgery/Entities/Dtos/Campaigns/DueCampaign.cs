namespace PlasticSurgery.Entities.Dtos.Campaigns;

/// <summary>A campaign the background sender should work on now: a scheduled one whose time has come, or a running one with
/// messages still to send. Status is what it was when listed (the sender re-checks before every message).</summary>
public record DueCampaign(Guid Id, Guid ClinicId, string Status);
