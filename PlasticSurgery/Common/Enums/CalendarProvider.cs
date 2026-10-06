namespace PlasticSurgery.Common.Enums;

public static class CalendarProvider
{
    public const string Google = "google";
    public const string Outlook = "outlook";
    public static readonly IReadOnlyList<string> All = new[] { Google, Outlook };
}
