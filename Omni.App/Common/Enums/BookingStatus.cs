namespace Omni.App.Common.Enums;

public static class BookingStatus
{
    public const string Booked = "booked";
    public const string Confirmed = "confirmed";
    public const string Attended = "attended";
    public const string NoShow = "no_show";
    public const string Canceled = "canceled";
    public const string Rescheduled = "rescheduled";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        Booked, Confirmed, Attended, NoShow, Canceled, Rescheduled
    };

    /// <summary>Whether a booking in this status occupies its time slot. Canceled frees it; Rescheduled means the
    /// booking moved elsewhere. Everything else (booked, confirmed, attended, no-show) holds the time.</summary>
    public static bool BlocksTime(string status) => status != Canceled && status != Rescheduled;

    /// <summary>The statuses a member (or the admin portal) can set from <paramref name="current"/> (QA t5-ops-03). A booked
    /// or confirmed booking can be confirmed, unconfirmed, marked attended or no-show, or canceled; an outcome can be
    /// corrected (attended ↔ no-show); canceled and rescheduled are final. "rescheduled" is never set by hand: moving a
    /// booking keeps it and changes its time.</summary>
    public static IReadOnlyList<string> NextFrom(string current) => current switch
    {
        Booked => [Confirmed, Attended, NoShow, Canceled],
        Confirmed => [Booked, Attended, NoShow, Canceled],
        Attended => [NoShow],
        NoShow => [Attended],
        _ => []
    };

    /// <summary>Whether <paramref name="to"/> is an outcome, which can only be set once the booking has started.</summary>
    public static bool IsOutcome(string to) => to is Attended or NoShow;
}
