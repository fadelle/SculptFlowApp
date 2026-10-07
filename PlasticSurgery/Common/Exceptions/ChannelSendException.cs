namespace PlasticSurgery.Common.Exceptions;

/// <summary>Base for a failed delivery attempt to an external messaging platform — controllers map
/// it to 502 Bad Gateway. WhatsApp and Telegram failures both derive from it.</summary>
public class ChannelSendException : Exception
{
    public ChannelSendException(string message) : base(message) { }
}
