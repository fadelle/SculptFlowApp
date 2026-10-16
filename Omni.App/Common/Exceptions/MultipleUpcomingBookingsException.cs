using Omni.App.Entities.Responses.Bookings;

namespace Omni.App.Common.Exceptions;

/// <summary>The contact has more than one upcoming booking and no bookingId was given, so reschedule/cancel can't tell which one to act on.
/// Bookings lists them (with ids) so the AI can ask the contact and retry with bookingId.</summary>
public class MultipleUpcomingBookingsException : Exception
{
    public MultipleUpcomingBookingsException(IReadOnlyList<UpcomingBookingResponse> bookings)
        : base("This contact has more than one upcoming booking. Ask which one, then repeat the request with its bookingId.")
    {
        Bookings = bookings;
    }

    public IReadOnlyList<UpcomingBookingResponse> Bookings { get; }
}
