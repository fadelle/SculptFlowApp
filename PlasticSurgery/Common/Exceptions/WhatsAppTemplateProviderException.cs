namespace PlasticSurgery.Common.Exceptions;

/// <summary>The provider refused a template. Message is shown to clinic staff, so it must be provider-neutral
/// (it never names Infobip — see the white-label rule in PROJECT_HANDOFF.md §29).</summary>
public class WhatsAppTemplateProviderException : Exception
{
    public WhatsAppTemplateProviderException(string message) : base(message) { }
}
