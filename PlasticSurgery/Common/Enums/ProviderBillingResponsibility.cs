namespace PlasticSurgery.Common.Enums;

/// <summary>Who pays the upstream communication provider for a connected channel account. Must match schema.sql.</summary>
public static class ProviderBillingResponsibility
{
    /// <summary>The customer pays the provider directly (e.g. their own WABA with their own Meta payment method).</summary>
    public const string CustomerDirect = "customer_direct";
    /// <summary>SculptFlow pays the provider (its Infobip account, an SMS aggregator) and charges the clinic its rates.</summary>
    public const string PlatformFunded = "platform_funded";
    /// <summary>The customer has its own account with another provider, which bills the customer.</summary>
    public const string ExternalProviderDirect = "external_provider_direct";
    /// <summary>No per-message provider fee for normal use (Telegram, Messenger, TikTok conversations...).</summary>
    public const string NoProviderUsageFee = "no_provider_usage_fee";

    public static readonly IReadOnlyList<string> All = new[] { CustomerDirect, PlatformFunded, ExternalProviderDirect, NoProviderUsageFee };

    public static bool IsValid(string? value) => value is not null && All.Contains(value);

    /// <summary>Admin-facing wording.</summary>
    public static string AdminLabel(string value) => value switch
    {
        CustomerDirect => "Customer pays provider directly",
        PlatformFunded => "SculptFlow pays provider",
        ExternalProviderDirect => "Customer pays external provider",
        NoProviderUsageFee => "No provider usage fee",
        _ => value
    };
}
