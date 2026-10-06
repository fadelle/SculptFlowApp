namespace PlasticSurgery.Entities.Responses.Dashboard;

/// <summary>Page 1 — Dashboard. Counts cover the requested date range (all time when none is given).</summary>
public record DashboardSummaryResponse(
    int NewInterestedPeople,
    int ConsultationsBooked,
    int ConsultationsAttended,
    int SurgeriesBooked,
    decimal Revenue,
    int OldLeadsRecovered
);
