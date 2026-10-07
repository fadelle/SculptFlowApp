namespace PlasticSurgery.Common.Exceptions;

public class WhatsAppSendException : ChannelSendException
{
    public WhatsAppSendException(string message) : base(message) { }
}
