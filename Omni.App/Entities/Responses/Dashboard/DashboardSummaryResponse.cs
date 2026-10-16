namespace Omni.App.Entities.Responses.Dashboard;

/// <summary>Page 1 — Dashboard. Counts cover the requested date range (all time when none is given).</summary>
public record DashboardSummaryResponse(
    int NewInterestedPeople,
    int BookingsMade,
    int BookingsAttended,
    int SurgeriesBooked,
    decimal Revenue,
    int OldLeadsRecovered
);
