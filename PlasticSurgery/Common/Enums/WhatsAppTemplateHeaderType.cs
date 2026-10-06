namespace PlasticSurgery.Common.Enums;

public static class WhatsAppTemplateHeaderType
{
    public const string None = "none";
    public const string Text = "text";
    public const string Image = "image";
    public const string Video = "video";
    public const string Document = "document";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { None, Text, Image, Video, Document };
}
