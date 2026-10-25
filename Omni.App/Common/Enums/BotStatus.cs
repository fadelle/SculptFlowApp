namespace Omni.App.Common.Enums;

/// <summary>crm.bots.status. Only an active bot answers contacts; a draft or inactive bot can still be edited and tested.</summary>
public static class BotStatus
{
    public const string Draft = "draft";
    public const string Active = "active";
    public const string Inactive = "inactive";

    public static readonly IReadOnlyList<string> All = [Draft, Active, Inactive];
}
