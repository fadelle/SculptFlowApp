using Microsoft.Extensions.Options;

namespace PlasticSurgery.Common.Configs;

/// <summary>"Billing:ProviderBilling" config: defaults for who pays the upstream provider, overriding the built-in
/// ones. Keys are a channel ("sms") or "{channel}_{provider}" ("whatsapp_meta"); values are a
/// ProviderBillingResponsibility. Example: set Defaults:whatsapp_meta = customer_direct once clinics connect their own
/// WABAs (Embedded Signup) and pay Meta themselves.</summary>
public class ProviderBillingOptions
{
    public Dictionary<string, string>? Defaults { get; set; }
}
