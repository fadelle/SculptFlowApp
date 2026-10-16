namespace Omni.App.Common.Exceptions;

public class WhatsAppSendException : ChannelSendException
{
    public WhatsAppSendException(string message) : base(message) { }
}
