using Omni.App.Common.Enums;

namespace Omni.App.Common.Helpers;

/// <summary>A channel's brand name as people see it ("whatsapp" → "WhatsApp"). Brand names aren't translated.</summary>
public static class ChannelNames
{
    public static string Brand(string channel) => channel switch
    {
        ChannelType.WhatsApp => "WhatsApp",
        ChannelType.Telegram => "Telegram",
        ChannelType.Facebook => "Facebook",
        ChannelType.Instagram => "Instagram",
        _ => channel.Length == 0 ? channel : char.ToUpperInvariant(channel[0]) + channel[1..]
    };
}
