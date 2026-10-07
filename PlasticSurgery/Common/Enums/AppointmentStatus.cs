namespace PlasticSurgery.Common.Enums;

public static class AppointmentStatus
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

    /// <summary>Whether an appointment in this status occupies its time slot. Canceled frees it; Rescheduled means the
    /// booking moved elsewhere. Everything else (booked, confirmed, attended, no-show) holds the time.</summary>
    public static bool BlocksTime(string status) => status != Canceled && status != Rescheduled;
}
