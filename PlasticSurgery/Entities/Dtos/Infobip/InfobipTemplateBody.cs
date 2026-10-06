using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlasticSurgery.Entities.Dtos.Infobip;

public record InfobipTemplateBody(
    [property: JsonPropertyName("placeholders")] IReadOnlyList<string> Placeholders,
    [property: JsonPropertyName("type")] string Type = "POSITIONAL_PARAMETERS");
