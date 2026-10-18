namespace Omni.App.Entities.Dtos.Billing;

/// <summary>A connected messaging account and who pays for its messages, in client-facing words (PaidBy, English for the
/// admin portal; the Billing page words it from ProviderBilling).</summary>
public record ClientChannelBillingRow(string Channel, string ChannelLabel, string? DisplayName, string PaidBy, bool ChargedByPlatform,
    string ProviderBilling);
