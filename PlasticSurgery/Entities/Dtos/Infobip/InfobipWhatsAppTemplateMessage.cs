using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlasticSurgery.Entities.Dtos.Infobip;

public record InfobipWhatsAppTemplateMessage(
    [property: JsonPropertyName("from")] string From,
    [property: JsonPropertyName("to")] string To,
    [property: JsonPropertyName("messageId")] string MessageId,
    [property: JsonPropertyName("content")] InfobipTemplateContent Content,
    [property: JsonPropertyName("notifyUrl")] string? NotifyUrl = null,
    [property: JsonPropertyName("callbackData")] string? CallbackData = null);
