using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Persistence.Contracts.Procedures;

public interface IProcedureBookingRepository
{
    /// <summary>Tracked.</summary>
    Task<ProcedureBooking?> GetAsync(Guid clinicId, Guid bookingId, CancellationToken ct = default);

    /// <summary>Newest first, with Lead and Procedure loaded.</summary>
    Task<(IReadOnlyList<ProcedureBooking> Items, int TotalCount)> ListAsync(Guid clinicId, string? status, int skip, int take,
        CancellationToken ct = default);

    void Add(ProcedureBooking booking);

    /// <summary>Loads booking.Lead and booking.Procedure.</summary>
    Task LoadDetailsAsync(ProcedureBooking booking, CancellationToken ct = default);
}
