using Omni.App.Entities.Models;
using Omni.App.Entities.Responses.Bookings;

namespace Omni.App.Business.Mappers.Bookings;

public static class BookingMapper
{
    /// <summary>Expects Contact (and Service, when set) to be loaded.</summary>
    public static BookingResponse ToResponse(Booking a) => new(
        a.Id, a.ClientId, a.ContactId, a.Contact?.FullName, a.ServiceId, a.Service?.Name,
        a.BookingType, a.Status, a.ScheduledStart, a.ScheduledEnd,
        a.LocationType, a.LocationName, a.Notes, a.CreatedAt
    );
}
