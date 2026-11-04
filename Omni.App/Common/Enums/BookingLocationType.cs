namespace Omni.App.Common.Enums;

/// <summary>Where a booking happens (scheduling.bookings.location_type), as the booking dialog offers it. Null = not set.</summary>
public static class BookingLocationType
{
    public const string InPerson = "in_person";
    public const string Video = "video";
    public const string Phone = "phone";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { InPerson, Video, Phone };
}
