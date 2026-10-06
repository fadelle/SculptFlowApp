using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlasticSurgery.Entities.Dtos.Infobip;

public record InfobipTemplateContent(
    [property: JsonPropertyName("templateName")] string TemplateName,
    [property: JsonPropertyName("templateData")] InfobipTemplateData TemplateData,
    [property: JsonPropertyName("language")] string Language);
