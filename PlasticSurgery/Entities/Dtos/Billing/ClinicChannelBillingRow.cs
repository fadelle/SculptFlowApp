namespace PlasticSurgery.Entities.Dtos.Billing;

/// <summary>A connected messaging account and who pays for its messages, in clinic-facing words.</summary>
public record ClinicChannelBillingRow(string Channel, string ChannelLabel, string? DisplayName, string PaidBy, bool ChargedBySculptFlow);
