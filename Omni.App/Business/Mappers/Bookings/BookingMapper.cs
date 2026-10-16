using Omni.App.Entities.Models;
using Omni.App.Entities.Responses.Bookings;

namespace Omni.App.Business.Mappers.Bookings;

public static class BookingMapper
{
    /// <summary>Expects Lead (and Service, when set) to be loaded.</summary>
    public static BookingResponse ToResponse(Booking a) => new(
        a.Id, a.ClientId, a.LeadId, a.Lead?.FullName, a.ServiceId, a.Service?.Name,
        a.BookingType, a.Status, a.ScheduledStart, a.ScheduledEnd,
        a.LocationType, a.LocationName, a.Notes, a.CreatedAt
    );
}
