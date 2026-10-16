namespace Omni.App.Common.Enums;

/// <summary>Upstream provider names recorded by billing (rate cards, usage records, provider-billing defaults).
/// Each channel has exactly one provider; WhatsApp's is Meta (Cloud API directly).</summary>
public static class ChannelProvider
{
    public const string Meta = "meta";
}
