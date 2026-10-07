namespace PlasticSurgery.Entities.Dtos.WhatsApp;

/// <summary>An approved template to send — provider-neutral. BodyParameters are the positional {{1}}, {{2}}...
/// values for the template body, in order. Header/button parameters and media get added here when needed.</summary>
public record WhatsAppTemplateSend(string Name, string LanguageCode, IReadOnlyList<string> BodyParameters);
