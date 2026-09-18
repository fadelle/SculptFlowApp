namespace PlasticSurgery.Dtos;

/// <summary>Page 1 — Main Numbers.</summary>
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
