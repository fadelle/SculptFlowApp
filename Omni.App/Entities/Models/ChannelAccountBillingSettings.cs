namespace Omni.App.Entities.Models;

/// <summary>Per connected channel account billing settings (billing.channel_account_settings): overrides the
/// channel/provider defaults for who pays the provider and whether Omni charges usage. Null = default.</summary>
public class ChannelAccountBillingSettings
{
    /// <summary>The connected account: channel_integrations.id (one row per account).</summary>
    public Guid ChannelIntegrationId { get; set; }
    public Guid ClinicId { get; set; }
    /// <summary>One of <see cref="ProviderBillingResponsibility"/>, or null for the default.</summary>
    public string? ProviderBilling { get; set; }
    /// <summary>Does Omni charge usage on this account? Null = the default (yes only when Omni pays the provider).</summary>
    public bool? OmniUsageBilling { get; set; }
    public string? Reason { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
