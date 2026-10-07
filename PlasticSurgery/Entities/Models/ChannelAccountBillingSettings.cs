namespace PlasticSurgery.Entities.Models;

/// <summary>Per connected channel account billing settings (billing.channel_account_settings): overrides the
/// channel/provider defaults for who pays the provider and whether SculptFlow charges usage. Null = default.</summary>
public class ChannelAccountBillingSettings
{
    /// <summary>The connected account: channel_integrations.id (one row per account).</summary>
    public Guid ChannelIntegrationId { get; set; }
    public Guid ClinicId { get; set; }
    /// <summary>One of <see cref="ProviderBillingResponsibility"/>, or null for the default.</summary>
    public string? ProviderBilling { get; set; }
    /// <summary>Does SculptFlow charge usage on this account? Null = the default (yes only when SculptFlow pays the provider).</summary>
    public bool? OmniUsageBilling { get; set; }
    /// <summary>The override only holds while the account is connected through this provider (null = any), so a
    /// reconnect through another provider falls back to the defaults instead of inheriting the wrong arrangement.</summary>
    public string? AppliesToProvider { get; set; }
    public string? Reason { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
