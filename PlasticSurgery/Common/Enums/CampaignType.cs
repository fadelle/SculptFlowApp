namespace PlasticSurgery.Common.Enums;

/// <summary>Extensible — no DB CHECK constraint (see Campaign.CampaignType).</summary>
public static class CampaignType
{
    public const string Reactivation = "reactivation";
    public const string FollowUp = "follow_up";
    public const string Promotion = "promotion";
    public const string Reminder = "reminder";
    public const string Custom = "custom";
}
