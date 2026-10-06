namespace PlasticSurgery.Entities.Dtos.Dashboard;

public record DashboardSummaryCounts(int NewInterestedPeople, int ConsultationsBooked, int ConsultationsAttended,
    int SurgeriesBooked, decimal Revenue);
