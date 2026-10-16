namespace Omni.App.Entities.Dtos.Dashboard;

public record DashboardSummaryCounts(int NewInterestedPeople, int BookingsMade, int BookingsAttended,
    int OrdersMade, decimal Revenue);
