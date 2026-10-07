using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlasticSurgery.Common.Exceptions;

/// <summary>A failed Telegram Bot API call. The message never contains the bot token.</summary>
public class TelegramApiException : ChannelSendException
{
    public int ErrorCode { get; }

    public TelegramApiException(string message, int errorCode = 0) : base(message)
    {
        ErrorCode = errorCode;
    }
}
