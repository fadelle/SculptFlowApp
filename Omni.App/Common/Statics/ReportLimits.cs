namespace Omni.App.Common.Statics;

/// <summary>Fixed shape of the Reports page (ReportService).</summary>
public static class ReportLimits
{
    /// <summary>Most campaigns listed for one period (newest first).</summary>
    public const int CampaignRows = 20;

    /// <summary>Longest period whose activity is shown day by day; longer or open-ended periods are shown by month.</summary>
    public const int MaxDailyActivityDays = 62;

    /// <summary>"Answered within N minutes" on the response times card.</summary>
    public const int FastReplyMinutes = 5;
}
