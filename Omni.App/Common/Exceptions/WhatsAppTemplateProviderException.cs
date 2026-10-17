namespace Omni.App.Common.Exceptions;

/// <summary>Meta refused a template. The message is stored as the template's rejection reason and shown to members.</summary>
public class WhatsAppTemplateProviderException : Exception
{
    public WhatsAppTemplateProviderException(string message) : base(message) { }
}
