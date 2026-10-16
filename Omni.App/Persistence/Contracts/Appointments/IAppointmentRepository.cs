using Omni.App.Entities.Dtos.Appointments;
using Omni.App.Entities.Models;

namespace Omni.App.Persistence.Contracts.Appointments;

/// <summary>Appointments. Returned entities are tracked unless the method says read-only.</summary>
public interface IAppointmentRepository
{
    void Add(Appointment appointment);

    Task<Appointment?> GetAsync(Guid clientId, Guid appointmentId, CancellationToken ct = default);

    /// <summary>With Lead and Procedure loaded.</summary>
    Task<Appointment?> GetWithDetailsAsync(Guid clientId, Guid appointmentId, CancellationToken ct = default);

    Task<Appointment?> GetForLeadAsync(Guid clientId, Guid leadId, Guid appointmentId, CancellationToken ct = default);

    /// <summary>Read-only, with Procedure loaded.</summary>
    Task<Appointment?> GetWithProcedureReadOnlyAsync(Guid appointmentId, CancellationToken ct = default);

    /// <summary>Read-only, with Procedure: the lead's booked/confirmed appointments starting after <paramref name="now"/>, soonest first.</summary>
    Task<IReadOnlyList<Appointment>> ListUpcomingForLeadAsync(Guid clientId, Guid leadId, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>With Lead and Procedure, ordered by start time.</summary>
    Task<(IReadOnlyList<Appointment> Items, int TotalCount)> ListAsync(Guid clientId, string? status, DateTimeOffset? from,
        DateTimeOffset? to, int skip, int take, CancellationToken ct = default);

    /// <summary>With Lead and Procedure: appointments starting in [fromUtc, toUtc), ordered by start time.</summary>
    Task<IReadOnlyList<Appointment>> ListStartingBetweenAsync(Guid clientId, DateTimeOffset fromUtc, DateTimeOffset toUtc,
        CancellationToken ct = default);

    /// <summary>Past appointments still booked/confirmed (no outcome recorded yet).</summary>
    Task<int> CountNeedingOutcomeAsync(Guid clientId, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Appointments that block slots (not canceled/rescheduled) starting in [lowUtc, highUtc).</summary>
    Task<IReadOnlyList<BusyAppointmentRow>> ListBusyAsync(Guid clientId, DateTimeOffset lowUtc, DateTimeOffset highUtc,
        Guid? excludeAppointmentId, CancellationToken ct = default);

    /// <summary>Loads Lead, and Procedure when set.</summary>
    Task LoadDetailsAsync(Appointment appointment, CancellationToken ct = default);

    /// <summary>Serializes bookings per client until the surrounding transaction ends (Postgres advisory lock).</summary>
    Task LockClientForBookingAsync(Guid clientId, CancellationToken ct = default);
}
