using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Common.Enums;

/// <summary>Values for ChannelIntegration.Provider — who actually carries a channel's traffic. Which one
/// WhatsApp uses is a global switch (WhatsApp:Provider config); a row only works while its own provider is
/// the active one.</summary>
public static class ChannelProvider
{
    public const string Meta = "meta";
    public const string Infobip = "infobip";

    /// <summary>The row's provider, treating the legacy null as Meta.</summary>
    public static string Of(ChannelIntegration integration) =>
        string.IsNullOrWhiteSpace(integration.Provider) ? Meta : integration.Provider;
}
