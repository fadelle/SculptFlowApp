namespace PlasticSurgery.Entities.Responses.Dashboard;

/// <summary>Dashboard "Needs attention" row — things staff should act on now, regardless of the selected period.</summary>
public record DashboardAttentionResponse(
    /// <summary>Active conversations the AI isn't handling on its own (staff mode or awaiting approval).</summary>
    int ConversationsNeedingStaff,
    /// <summary>Leads whose next follow-up date has passed and who aren't closed out (lost / not interested / surgery booked).</summary>
    int OverdueFollowups,
    /// <summary>Past appointments still booked/confirmed — no attended / no-show / canceled recorded yet.</summary>
    int AppointmentsNeedingOutcome
);
