namespace Omni.App.Entities.Dtos.Bookings;

/// <summary>A weekday's hours as members edit them. DayOfWeek uses .NET numbering (0 = Sunday). Times are "HH:mm" client-local.
/// Start2/End2: an optional second window the same day, after the first (a lunch break or split shift, QA t5-ops-18).</summary>
public record DayRuleDto(int DayOfWeek, bool IsOpen, string Start, string End, string? Start2 = null, string? End2 = null);
