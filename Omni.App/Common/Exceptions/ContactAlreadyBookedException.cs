using Omni.App.Entities.Responses.Bookings;

namespace Omni.App.Common.Exceptions;

/// <summary>The contact already has an upcoming booked/confirmed booking; book_slot refuses to make a second one.
/// Existing describes it so the AI can offer to move it instead.</summary>
public class ContactAlreadyBookedException : Exception
{
    public ContactAlreadyBookedException(UpcomingBookingResponse existing)
        : base($"This contact already has an upcoming booking ({existing.Label}). Reschedule or cancel it instead of booking another.")
    {
        Existing = existing;
    }

    public UpcomingBookingResponse Existing { get; }
}
