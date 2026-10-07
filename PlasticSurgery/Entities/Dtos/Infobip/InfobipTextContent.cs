using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlasticSurgery.Entities.Dtos.Infobip;

public record InfobipTextContent(
    [property: JsonPropertyName("text")] string Text);
