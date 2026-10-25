using System.Globalization;

namespace Omni.App.Common.Helpers;

/// <summary>How the AI tools name a booking time ("Monday, Sep 28 at 3:00 PM", in the client's local time): the AI reads it
/// and words it for the contact, so it stays invariant English.</summary>
public static class BookingLabels
{
    public static string For(DateTimeOffset local) =>
        $"{local.DayOfWeek}, {local.ToString("MMM d", CultureInfo.InvariantCulture)} at {local.ToString("h:mm tt", CultureInfo.InvariantCulture)}";
}
