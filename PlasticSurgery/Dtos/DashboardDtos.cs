namespace PlasticSurgery.Dtos;

/// <summary>Page 1 — Dashboard. Counts cover the requested date range (all time when none is given).</summary>
public record DashboardSummaryResponse(
    int NewInterestedPeople,
    int ConsultationsBooked,
    int ConsultationsAttended,
    int SurgeriesBooked,
    decimal Revenue,
    int OldLeadsRecovered
);

/// <summary>Page 2 — Interested People (leads list) row.</summary>
public record DashboardLeadRow(
    Guid Id,
    string? FullName,
    string? Phone,
    string? ProcedureName,
    string? Source,
    string Status,
    string QualificationStatus,
    DateTimeOffset? LastContactAt,
    DateTimeOffset? NextFollowupAt,
    DateTimeOffset CreatedAt
);

/// <summary>Page 3 — Appointments row.</summary>
public record DashboardAppointmentRow(
    Guid Id,
    string? LeadFullName,
    string? LeadPhone,
    string? ProcedureName,
    string Status,
    DateTimeOffset ScheduledStart,
    string? LocationType
);

/// <summary>Page 4 — Surgeries & Money, sliced by procedure.</summary>
public record DashboardProcedureRow(
    Guid ProcedureId,
    string ProcedureName,
    int LeadCount,
    int BookingCount,
    int CompletedCount,
    decimal CompletedRevenue
);

/// <summary>Dashboard "Needs attention" row — things staff should act on now, regardless of the selected period.</summary>
public record DashboardAttentionResponse(
    /// <summary>Active conversations the AI isn't handling on its own (staff mode or awaiting approval).</summary>
    int ConversationsNeedingStaff,
    /// <summary>Leads whose next follow-up date has passed and who aren't closed out (lost / not interested / surgery booked).</summary>
    int OverdueFollowups,
    /// <summary>Past appointments still booked/confirmed — no attended / no-show / canceled recorded yet.</summary>
    int AppointmentsNeedingOutcome
);
